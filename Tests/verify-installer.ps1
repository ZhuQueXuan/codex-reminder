[CmdletBinding()]
param([string]$ReleaseVersion = 'v0.2')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $root ('Releases\Codex提醒助手-安装程序-' + $ReleaseVersion + '.exe')
$archive = Join-Path $root ('Releases\Codex提醒助手-' + $ReleaseVersion + '-Windows.zip')
$scratch = Join-Path $root ('work\installer-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$destination = Join-Path $scratch '接收者 安装目录'
$expected = Join-Path $scratch 'expected'
New-Item -ItemType Directory -Path $destination,$expected -Force | Out-Null
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Run-Package {
    $process = Start-Process -FilePath $installer -ArgumentList @('--extract-only', ('"' + $destination + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Extraction timed out' }
    Assert ($process.ExitCode -eq 0) 'EXE extraction failed'
}
Assert ((Get-FileHash -LiteralPath $installer).Hash -eq ([IO.File]::ReadAllText($installer + '.sha256').Split(' ')[0])) 'Installer checksum mismatch'
Run-Package
Expand-Archive -LiteralPath $archive -DestinationPath $expected
$expectedRoot = Join-Path $expected 'Codex提醒助手'
$files = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
Assert ($files.Count -eq 15) 'Incomplete EXE payload'
foreach ($file in $files) {
    $relative = $file.FullName.Substring($destination.Length + 1)
    Assert ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $expectedRoot $relative)).Hash) ('EXE payload differs: ' + $relative)
}
$settingsPath = Join-Path $destination 'Config\settings.json'
[IO.File]::WriteAllText($settingsPath, '{"enabled":false,"sound":false,"require_tray":true,"notify_completed":false,"notify_approval":true,"notify_question":false,"custom":"preserve"}')
$before = (Get-FileHash -LiteralPath $settingsPath).Hash
$unrelated = Join-Path $destination 'recipient-note.txt'
[IO.File]::WriteAllText($unrelated, 'preserve')
Run-Package
Assert ($before -eq (Get-FileHash -LiteralPath $settingsPath).Hash) 'Reinstall reset user preferences'
Assert ([IO.File]::ReadAllText($unrelated) -eq 'preserve') 'Reinstall modified unrelated files'
$fakeDesktop = Join-Path $scratch 'desktop'
New-Item -ItemType Directory -Path $fakeDesktop -Force | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom($installer)
$method = $assembly.GetType('CodexReminderInstaller.InstallerForm').GetMethod('CreateShortcut', [Reflection.BindingFlags]'NonPublic,Static')
$null = $method.Invoke($null, @([string]$destination, [string]$fakeDesktop))
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $fakeDesktop 'Codex提醒助手.lnk'))
try {
    Assert ($link.TargetPath -eq (Join-Path $destination 'App\CodexReminder.exe')) 'Shortcut points to wrong executable'
    Assert ($link.WorkingDirectory -eq $destination) 'Shortcut working directory is wrong'
} finally {
    $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
$diagnosis = Join-Path $scratch 'diagnosis.json'
$process = Start-Process -FilePath (Join-Path $destination 'App\CodexReminder.exe') -ArgumentList @('--diagnose', ('"' + $diagnosis + '"')) -WindowStyle Hidden -Wait -PassThru
Assert ($process.ExitCode -eq 0) 'Installed executable cannot load'
$state = Get-Content -LiteralPath $diagnosis -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (-not $state.sound -and -not $state.notify_completed -and $state.notify_approval -and -not $state.notify_question -and -not $state.tray_running) 'Installed app cannot read preserved settings'
$fakeHome = Join-Path $scratch 'isolated-codex-home'
$runtime = Join-Path $scratch 'runtime'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$result = & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $destination 'Src\Manage.ps1') -Action Install -ConfigDirectory $fakeHome -RuntimeDirectory $runtime -Quiet
Assert ($LASTEXITCODE -eq 0) 'Isolated installation failed'
$hooks = Get-Content -LiteralPath (Join-Path $fakeHome 'hooks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($hooks.hooks.Stop[0].hooks[0].command.Contains((Join-Path $destination 'Attention.ps1'))) 'Hook points to a temporary payload location'
$after = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (-not $after.sound -and -not $after.notify_completed -and $after.notify_approval -and -not $after.notify_question -and $after.custom -eq 'preserve') 'Setup changed event preferences'
[pscustomobject]@{ singleExeExtraction = 'passed'; files = 15; chineseAndSpacedDirectory = 'passed'; repeatPreservesSettings = 'passed'; unrelatedFilesPreserved = 'passed'; shortcutInIsolatedDirectory = 'passed'; installedAppLoads = 'passed'; isolatedHookInstall = 'passed'; actualDesktopAndCodexConfiguration = 'not-modified'; recipientInteractiveInstall = 'not-tested' } | ConvertTo-Json
