[CmdletBinding()]
param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ArchivePath) { $ArchivePath = Join-Path $root 'Releases\Codex提醒助手-2026.10.13.3-Windows.zip' }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$scratch = Join-Path $root ('work\package-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$unpacked = Join-Path $scratch '接收者 空格目录'
New-Item -ItemType Directory -Path $unpacked -Force | Out-Null
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $unpacked
$package = Join-Path $unpacked 'Codex提醒助手'
$settingsPath = Join-Path $package 'Config\settings.json'
$initial = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (-not $initial.enabled -and $initial.sound -and $initial.require_tray) 'Unexpected distributable settings'
Assert ($initial.notify_completed -and $initial.notify_approval -and $initial.notify_question) 'Packaged reminder types should default to all enabled'
foreach ($relative in @('首次安装并启动.cmd','启动提醒助手.cmd','打开事件审阅终端.cmd','先看这里.txt','Docs\使用说明.md','Src\Setup.ps1','App\CodexReminder.exe.config')) {
    Assert (Test-Path -LiteralPath (Join-Path $package $relative)) ('Missing dependency: ' + $relative)
}
Assert ((Get-FileHash -LiteralPath (Join-Path $package 'App\CodexReminder.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $root 'App\Codex提醒助手.exe')).Hash) 'Packaged executable differs from deployed build'
Assert ((Get-FileHash -LiteralPath $ArchivePath).Hash -eq ([IO.File]::ReadAllText($ArchivePath + '.sha256').Split(' ')[0])) 'Archive checksum mismatch'
$allFiles = @(Get-ChildItem -LiteralPath $package -File -Recurse)
Assert ($allFiles.Count -eq 15) 'Unexpected files in distribution'
$parseCount = 0
foreach ($file in $allFiles) {
    if ($file.Extension -eq '.ps1') {
        $tokens = $null; $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        Assert ($errors.Count -eq 0) ('PowerShell parse failure: ' + $file.Name)
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        Assert ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) 'Missing UTF-8 BOM for Windows PowerShell'
        $parseCount++
    }
    if ($file.Extension -in @('.ps1','.cs','.json','.md','.txt','.cmd','.config')) {
        $text = [IO.File]::ReadAllText($file.FullName)
        Assert (-not $text.Contains($root) -and -not $text.Contains($env:USERPROFILE)) ('Sender path in distribution: ' + $file.Name)
    }
}
# Install against an isolated configuration only; never change the actual Codex configuration.
$fakeHome = Join-Path $scratch 'isolated-codex-home'
$runtime = Join-Path $scratch 'runtime'
New-Item -ItemType Directory -Path $fakeHome -Force | Out-Null
$hooksPath = Join-Path $fakeHome 'hooks.json'
[IO.File]::WriteAllText($hooksPath, '{"metadata":{"preserved":true},"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo existing","statusMessage":"Existing"}]}]}}')
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
1..2 | ForEach-Object {
    $installed = & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $package 'Src\Manage.ps1') -Action Install -ConfigDirectory $fakeHome -RuntimeDirectory $runtime -Quiet
    Assert ($LASTEXITCODE -eq 0) 'Relocated install failed'
}
$hooks = Get-Content -LiteralPath $hooksPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($hooks.metadata.preserved -and $hooks.hooks.Stop[0].hooks[0].command -eq 'echo existing') 'Existing recipient configuration was lost'
foreach ($name in @('Stop','PermissionRequest','PreToolUse')) {
    $ours = @($hooks.hooks.$name | ForEach-Object { $_.hooks } | Where-Object { $_.statusMessage -like 'CodexAttention:*' })
    Assert ($ours.Count -eq 1) 'Duplicate or missing reminder hook'
    Assert ($ours[0].command.Contains((Join-Path $package 'Attention.ps1'))) 'Hook points to sender location'
}
$diagnosis = Join-Path $scratch 'diagnosis.json'
$process = Start-Process -FilePath (Join-Path $package 'App\CodexReminder.exe') -ArgumentList @('--diagnose', ('"' + $diagnosis + '"')) -WindowStyle Hidden -Wait -PassThru
Assert ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $diagnosis)) 'Packaged executable could not load configuration'
$state = Get-Content -LiteralPath $diagnosis -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($state.enabled -and $state.require_tray -and -not $state.tray_running) 'Wrong relocated package identity or settings'
$eventFile = Join-Path $scratch 'event.json'
[IO.File]::WriteAllText($eventFile, '{"hook_event_name":"Stop"}')
$result = & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $package 'Attention.ps1') -PayloadFile $eventFile -DryRun | ConvertFrom-Json
Assert ($LASTEXITCODE -eq 0 -and $result.reason -eq 'completed' -and -not $result.wouldNotify) 'Extracted hook dependency or tray protection failed'
[pscustomobject]@{ files = $allFiles.Count; parsedScripts = $parseCount; relocation = 'passed'; isolatedInstall = 'passed'; existingHooksPreserved = 'passed'; privateDataExcluded = 'passed'; executableAndHookDependencies = 'passed'; actualRecipientComputer = 'not-tested' } | ConvertTo-Json
