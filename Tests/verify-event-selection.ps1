$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ('work\event-selection-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path (Join-Path $scratch 'Src'),(Join-Path $scratch 'Config') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Attention.ps1') -Destination $scratch
foreach ($name in @('Attention.ps1','Settings.ps1','FocusWindow.cs')) {
    Copy-Item -LiteralPath (Join-Path $root ('Src\' + $name)) -Destination (Join-Path $scratch 'Src')
}
$settings = Join-Path $scratch 'Config\settings.json'
$payload = Join-Path $scratch 'event.json'
$entry = Join-Path $scratch 'Attention.ps1'
$runtime = Join-Path $scratch 'runtime'
$hostExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Invoke-Event($event, [bool]$dryRun, [bool]$approvalSignal) {
    $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$entry,'-RuntimeDirectory',$runtime)
    if ($dryRun) { $arguments += '-DryRun' }
    if ($approvalSignal) { $arguments += '-ApprovalSignal' }
    else {
        [IO.File]::WriteAllText($payload, ($event | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($true)))
        $arguments += @('-PayloadFile',$payload)
    }
    $output = & $hostExe @arguments
    Assert ($LASTEXITCODE -eq 0) 'Event helper failed'
    return $output
}
$events = @(
    @{ data = @{ hook_event_name = 'Stop' }; key = 'notify_completed' },
    @{ data = @{ hook_event_name = 'PermissionRequest' }; key = 'notify_approval' },
    @{ data = @{ hook_event_name = 'PreToolUse'; tool_name = 'request_user_input_async' }; key = 'notify_question' }
)
$checked = 0
for ($mask = 0; $mask -lt 8; $mask++) {
    $configuration = @{ enabled = $true; sound = $true; require_tray = $false; notify_completed = [bool]($mask -band 1); notify_approval = [bool]($mask -band 2); notify_question = [bool]($mask -band 4) }
    [IO.File]::WriteAllText($settings, ($configuration | ConvertTo-Json))
    foreach ($event in $events) {
        $state = Invoke-Event $event.data $true $false | ConvertFrom-Json
        Assert ($state.wouldNotify -eq $configuration[$event.key]) ('Wrong selection for ' + $event.key)
        if (-not $configuration[$event.key]) {
            Assert ((Invoke-Event $event.data $false $false) -eq '{}') 'Suppressed event changed response contract'
            Assert (-not (Test-Path -LiteralPath $runtime)) 'Suppressed event performed a notification action'
        }
        $checked++
    }
    $approval = Invoke-Event $null $true $true | ConvertFrom-Json
    Assert ($approval.wouldNotify -eq $configuration.notify_approval) 'Observer approval ignored the category switch'
    if (-not $configuration.notify_approval) { Assert ((Invoke-Event $null $false $true) -eq '{}') 'Disabled observer approval was not suppressed' }
    $checked++
}
# Missing category fields in an old configuration retain previous behavior.
[IO.File]::WriteAllText($settings, '{"enabled":true,"sound":true,"require_tray":false}')
foreach ($event in $events) { Assert ((Invoke-Event $event.data $true $false | ConvertFrom-Json).wouldNotify) 'Old configuration changed behavior' }
[IO.File]::WriteAllText($settings, '{"enabled":false,"require_tray":false,"notify_completed":true,"notify_approval":true,"notify_question":true}')
foreach ($event in $events) { Assert (-not (Invoke-Event $event.data $true $false | ConvertFrom-Json).wouldNotify) 'Master switch was ignored' }
[IO.File]::WriteAllText($settings, '{"enabled":true,"require_tray":true,"notify_completed":true,"notify_approval":true,"notify_question":true}')
Assert (-not (Invoke-Event $events[0].data $true $false | ConvertFrom-Json).wouldNotify) 'Missing tray protection was ignored'
[pscustomobject]@{ combinations = 8; eventChecks = $checked; filteredEventsProduceNoActions = 'passed'; observerApprovalGate = 'passed'; oldSettingsCompatibility = 'passed'; masterAndResidentProtection = 'passed'; realWindowActions = 'none' } | ConvertTo-Json
