[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Diagnose,
    [switch]$DryRun,
    [switch]$ApprovalSignal,
    [string]$PayloadFile,
    [string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Settings.ps1')
$settingsPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Config\settings.json'
if (-not $RuntimeDirectory) {
    $RuntimeDirectory = Join-Path $env:LOCALAPPDATA 'CodexAttention'
}

function Get-AttentionReason($eventData) {
    switch ($eventData.hook_event_name) {
        'Stop' { return 'completed' }
        'PermissionRequest' { return 'approval' }
        'PreToolUse' {
            if ($eventData.tool_name -match '(^|[._])request_user_input(_async)?$') { return 'question' }
            return $null
        }
        default { return $null }
    }
}

function Test-EventEnabled($settings, [string]$reason) {
    $key = switch ($reason) {
        'completed' { 'notify_completed' }
        'approval' { 'notify_approval' }
        'question' { 'notify_question' }
    }
    if (-not $key) { return $true }
    $value = $settings.$key
    # Older configuration files keep all three categories enabled.
    if ($value -is [bool]) { return $value }
    return $true
}

$mutex = $null
$locked = $false
$stage = 'start'
$inputLength = 0
$eventInputEncoding = 'utf-8'
try {
    if ($Diagnose) {
        Add-Type -Path (Join-Path $PSScriptRoot 'FocusWindow.cs')
        $handle = [CodexAttentionWindow]::Find()
        [pscustomobject]@{ windowFound = ($handle -ne [IntPtr]::Zero); handle = $handle.ToInt64() } | ConvertTo-Json -Compress
        exit 0
    }
    $stage = 'read-settings'
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $stage = 'check-tray'
    $trayReady = (-not $settings.require_tray) -or (Test-ReminderTrayRunning)
    if ($ApprovalSignal) { $reason = 'approval' }
    elseif ($Test) { $reason = 'test' }
    else {
        $stage = 'read-event-input'
        if ($PayloadFile) { $raw = Get-Content -LiteralPath $PayloadFile -Raw -Encoding UTF8 }
        else {
            # Codex sends UTF-8 JSON. Console.In uses the Windows console code
            # page (often GB2312), which can corrupt Unicode and JSON delimiters.
            $inputReader = [IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false, $true), $true)
            try { $raw = $inputReader.ReadToEnd() }
            finally { $inputReader.Dispose() }
        }
        $inputLength = $raw.Length
        $stage = 'parse-event-input'
        $eventData = $raw | ConvertFrom-Json
        $reason = Get-AttentionReason $eventData
    }
    $categoryEnabled = Test-EventEnabled $settings $reason
    # PermissionRequest precedes the actual decision and may be auto-approved.
    # Never steal focus here; the tray observes a sustained pending decision.
    $approvalDeferred = ($reason -eq 'approval' -and -not $ApprovalSignal -and -not $Test)
    if ($DryRun) {
        [pscustomobject]@{ reason = $reason; categoryEnabled = $categoryEnabled; approvalDeferred = $approvalDeferred; wouldNotify = [bool]($reason -and $settings.enabled -and $trayReady -and $categoryEnabled -and -not $approvalDeferred); trayReady = $trayReady } | ConvertTo-Json -Compress
        exit 0
    }
    if (-not $reason -or $approvalDeferred -or (-not $Test -and (-not $settings.enabled -or -not $trayReady -or -not $categoryEnabled))) { [Console]::Out.WriteLine('{}'); exit 0 }
    $stage = 'prepare-runtime'
    New-Item -ItemType Directory -Path $RuntimeDirectory -Force | Out-Null
    $stage = 'notification-lock'
    $mutex = New-Object System.Threading.Mutex($false, 'Local\CodexAttention.Notification')
    try { $locked = $mutex.WaitOne($(if ($reason -eq 'approval') { 2000 } else { 0 })) }
    catch [System.Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { [Console]::Out.WriteLine('{}'); exit 0 }
    $stampPath = Join-Path $RuntimeDirectory 'last-alert.json'
    $now = [DateTimeOffset]::UtcNow
    if (-not $Test -and (Test-Path -LiteralPath $stampPath)) {
        try {
            $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
            if (($now - [DateTimeOffset]::Parse($stamp.at)).TotalSeconds -lt 2 -and ($reason -ne 'approval' -or $stamp.reason -eq 'approval')) {
                [Console]::Out.WriteLine('{}'); exit 0
            }
        } catch { }
    }
    $stage = 'compile-window-module'
    Add-Type -Path (Join-Path $PSScriptRoot 'FocusWindow.cs')
    $stage = 'find-window'
    $handle = [CodexAttentionWindow]::Find()
    $wasForeground = [CodexAttentionWindow]::IsForeground($handle)
    if ($handle -ne [IntPtr]::Zero -and -not $wasForeground) {
        [CodexAttentionWindow]::Restore($handle)
        # Windows Script Host activation proved more reliable on this desktop.
        try {
            $bridge = New-Object -ComObject WScript.Shell
            $null = $bridge.AppActivate([CodexAttentionWindow]::ProcessId($handle))
            Start-Sleep -Milliseconds 100
        } catch { }
    }
    $stage = 'reveal-window'
    $result = [CodexAttentionWindow]::Reveal($handle, 1200)
    if (-not $wasForeground -and $result -eq 'already-foreground') { $result = 'foreground' }
    $stage = 'save-result'
    @{ at = $now.ToString('o'); reason = $reason } | ConvertTo-Json | Set-Content -LiteralPath $stampPath -Encoding UTF8
    if ($settings.sound -and $result -notin @('already-foreground', 'window-not-found')) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Media.SystemSounds]::Asterisk.Play()
    }
    $record = @{ at = $now.ToString('o'); reason = $reason; result = $result }
    if ($ApprovalSignal) { $record.source = 'approval-interface-observer' }
    # No chat text, tool input, approval contents or credentials are logged.
    $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $RuntimeDirectory 'events.jsonl') -Encoding UTF8
    if ($Test) {
        $record | ConvertTo-Json -Compress
    } else {
        # Empty object never approves, denies or continues a turn.
        [Console]::Out.WriteLine('{}')
    }
} catch {
    if ($Test -or $Diagnose -or $DryRun) { Write-Error $_; exit 1 }
    # Notifications must never block Codex or change its approval decision.
    try {
        New-Item -ItemType Directory -Path $RuntimeDirectory -Force | Out-Null
        @{ at = [DateTimeOffset]::UtcNow.ToString('o'); result = 'helper-error'; errorType = $_.Exception.GetType().Name; stage = $stage; line = $_.InvocationInfo.ScriptLineNumber; inputLength = $inputLength; inputEncoding = $eventInputEncoding } |
            ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $RuntimeDirectory 'events.jsonl') -Encoding UTF8
    } catch { }
    [Console]::Out.WriteLine('{}')
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}
