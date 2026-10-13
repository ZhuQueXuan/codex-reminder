[CmdletBinding()]
param([string]$ConfigDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'Src\Settings.ps1')
$exe = Join-Path $root 'App\Codex提醒助手.exe'
$settingsPath = Join-Path $root 'Config\settings.json'
$scratch = Join-Path $root 'work\tray-test'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Command([string[]]$arguments) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Assert ($process.WaitForExit(10000)) 'Controller command timed out'
    Assert ($process.ExitCode -eq 0) ('Controller failed: ' + ($arguments -join ' '))
}
function Read-State {
    $report = Join-Path $scratch 'state.json'
    Command @('--diagnose', ('"' + $report + '"'))
    return Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Event-State {
    $payload = Join-Path $scratch 'event.json'
    @{ hook_event_name = 'Stop' } | ConvertTo-Json | Set-Content -LiteralPath $payload -Encoding UTF8
    & (Join-Path $root 'Attention.ps1') -PayloadFile $payload -DryRun | ConvertFrom-Json
}
Assert (-not (Test-ReminderTrayRunning)) 'An existing tray is running; test must not close a user instance'
$savedSettings = [IO.File]::ReadAllBytes($settingsPath)
$codexDirectory = if ($ConfigDirectory) { $ConfigDirectory } elseif ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$hooksPath = Join-Path $codexDirectory 'hooks.json'
$hooksHash = if (Test-Path -LiteralPath $hooksPath) { (Get-FileHash -LiteralPath $hooksPath).Hash } else { $null }
$trayProcess = $null
try {
    Set-ReminderSettings @{ tray_test_marker = 'preserve-this'; sound = $false; notify_completed = $true; notify_approval = $false; notify_question = $true }
    Command @('--disable')
    Assert (-not (Read-State).enabled) 'Disable controller failed'
    $trayProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        if (Test-ReminderTrayRunning) {
            $state = Read-State
            if ($state.enabled -and $state.require_tray) { $ready = $true; break }
        }
        Start-Sleep -Milliseconds 100
        Assert (-not $trayProcess.HasExited) 'Tray exited during startup'
    }
    Assert $ready 'Tray did not become ready'
    Assert (Event-State).wouldNotify 'PowerShell hook cannot detect live C# tray'
    Command @('--disable')
    $state = Read-State
    Assert (-not $state.enabled -and $state.tray_running) 'Pause must retain resident tray'
    Assert (-not (Event-State).wouldNotify) 'Paused hook still notifies'
    Command @('--enable')
    $state = Read-State
    Assert ($state.enabled -and $state.tray_running) 'Resume failed'
    Assert ($state.tray_test_marker -eq 'preserve-this' -and -not $state.sound) 'Settings update lost unrelated fields'
    Assert ($state.notify_completed -and -not $state.notify_approval -and $state.notify_question) 'Pause/resume reset reminder type preferences'
    $instances = @(Get-Process -Name 'Codex提醒助手' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    Assert ($instances.Count -eq 1) 'Repeated launch created extra resident instances'
    Command @('--exit')
    Assert ($trayProcess.WaitForExit(5000)) 'Exit command did not stop resident tray'
    $state = Read-State
    Assert (-not $state.tray_running -and -not $state.enabled) 'Exit did not stop reminders'
    Set-ReminderSettings @{ enabled = $true }
    Assert (-not (Event-State).wouldNotify) 'Missing resident must suppress reminders even if enabled is true'
    $afterHooksHash = if (Test-Path -LiteralPath $hooksPath) { (Get-FileHash -LiteralPath $hooksPath).Hash } else { $null }
    Assert ($hooksHash -eq $afterHooksHash) 'Registered hooks changed'
    [pscustomobject]@{ start = 'passed'; pauseResume = 'passed'; singleInstance = 'passed'; settingsPreserved = 'passed'; exitStopsReminders = 'passed'; missingResidentGate = 'passed'; registeredHooks = 'unchanged' } | ConvertTo-Json
} finally {
    if ($trayProcess -and -not $trayProcess.HasExited) {
        Command @('--exit')
        if (-not $trayProcess.WaitForExit(5000)) { Stop-Process -Id $trayProcess.Id }
    }
    [IO.File]::WriteAllBytes($settingsPath, $savedSettings)
}
