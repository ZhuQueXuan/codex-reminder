$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'Src'
$sandbox = Join-Path $root 'work\reminder-test'
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$entryPath = Join-Path $root 'Attention.ps1'
$settingsPath = Join-Path $root 'Config\settings.json'
$beforeSettings = [IO.File]::ReadAllBytes($settingsPath)
try {
$parseCount = 0
(@(Get-Item -LiteralPath $entryPath) + @(Get-ChildItem -LiteralPath $app -Filter '*.ps1')) | ForEach-Object {
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Assert ($errors.Count -eq 0) ('Parse failed: ' + $_.Name)
    $parseCount++
}
$cases = @(
    @{ input = @{ hook_event_name = 'Stop'; stop_hook_active = $false }; expected = 'completed' },
    @{ input = @{ hook_event_name = 'Stop'; stop_hook_active = $true }; expected = 'completed' },
    @{ input = @{ hook_event_name = 'PermissionRequest'; tool_name = 'Bash' }; expected = 'approval' },
    @{ input = @{ hook_event_name = 'PreToolUse'; tool_name = 'request_user_input' }; expected = 'question' },
    @{ input = @{ hook_event_name = 'PreToolUse'; tool_name = 'functions.request_user_input_async' }; expected = 'question' },
    @{ input = @{ hook_event_name = 'PreToolUse'; tool_name = 'Bash' }; expected = $null },
    @{ input = @{ hook_event_name = 'PostToolUse'; tool_name = 'Bash' }; expected = $null }
)
$payload = Join-Path $sandbox 'input.json'
foreach ($case in $cases) {
    $case.input | ConvertTo-Json | Set-Content -LiteralPath $payload -Encoding UTF8
    $result = & $entryPath -PayloadFile $payload -DryRun | ConvertFrom-Json
    Assert ($result.reason -eq $case.expected) ('Unexpected reason for ' + ($case.input | ConvertTo-Json -Compress))
}
$hooksPath = Join-Path $sandbox 'hooks.json'
@'
{"customMetadata":{"keep":true},"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo existing","statusMessage":"Existing"}]}],"SessionStart":[{"matcher":"startup","hooks":[{"type":"command","command":"echo startup"}]}]}}
'@ | Set-Content -LiteralPath $hooksPath -Encoding UTF8
& (Join-Path $app 'Manage.ps1') -Action Install -ConfigDirectory $sandbox -RuntimeDirectory (Join-Path $sandbox 'runtime') -Quiet | Out-Null
& (Join-Path $app 'Manage.ps1') -Action Install -ConfigDirectory $sandbox -RuntimeDirectory (Join-Path $sandbox 'runtime') -Quiet | Out-Null
$merged = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
Assert ($merged.customMetadata.keep -eq $true) 'Lost metadata'
Assert ($merged.hooks.SessionStart[0].hooks[0].command -eq 'echo startup') 'Lost existing event'
Assert (@($merged.hooks.Stop).Count -eq 2) 'Stop duplicated or original lost'
Assert ($merged.hooks.Stop[0].hooks[0].command -eq 'echo existing') 'Lost existing Stop command'
foreach ($name in @('Stop','PermissionRequest','PreToolUse')) {
    $ours = @($merged.hooks.$name | ForEach-Object { $_.hooks } | Where-Object { $_.statusMessage -eq 'CodexAttention: 显示 Codex 窗口' })
    Assert ($ours.Count -eq 1) ('Duplicated hook ' + $name)
    Assert ($ours[0].command.Contains('-WindowStyle Hidden')) 'Hook should not show terminal'
}
& (Join-Path $app 'Manage.ps1') -Action Disable -Quiet | Out-Null
@{ hook_event_name = 'Stop' } | ConvertTo-Json | Set-Content -LiteralPath $payload -Encoding UTF8
$paused = & $entryPath -PayloadFile $payload -DryRun | ConvertFrom-Json
Assert (-not $paused.wouldNotify) 'Paused hook still notifies'
$stdout = & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $entryPath -PayloadFile $payload
Assert ($stdout -eq '{}') 'Paused hook must return empty JSON'
& (Join-Path $app 'Manage.ps1') -Action Enable -Quiet | Out-Null
'malformed JSON' | Set-Content -LiteralPath $payload -Encoding UTF8
$stdout = & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $entryPath -PayloadFile $payload -RuntimeDirectory (Join-Path $sandbox 'runtime')
Assert ($stdout -eq '{}') 'Malformed input must not affect Codex decisions'
Add-Type -Path (Join-Path $app 'FocusWindow.cs')
Assert ([CodexAttentionWindow]::Reveal([IntPtr]::Zero,0) -eq 'window-not-found') 'Missing window not handled'
[pscustomobject]@{ parsedScripts = $parseCount; eventCases = $cases.Count; mergeAndIdempotency = 'passed'; pauseAndErrorContract = 'passed'; nativeCompilation = 'passed' } | ConvertTo-Json

} finally {
    [IO.File]::WriteAllBytes($settingsPath, $beforeSettings)
}