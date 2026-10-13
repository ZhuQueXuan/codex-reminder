$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$payloads = @(
    @{ hook_event_name = 'Stop'; last_assistant_message = '✅ 已完成。你可以右键开启提醒，或选择“关闭提醒”。'; expected = 'completed' },
    @{ hook_event_name = 'PermissionRequest'; tool_name = 'Bash'; tool_input = @{ description = '仅检查状态，不操作窗口。✅'; command = 'echo "example"' }; expected = 'approval' },
    @{ hook_event_name = 'PreToolUse'; tool_name = 'request_user_input_async'; tool_input = @{ questions = @(@{ title = '测试问题：现在继续吗？🙂' }) }; expected = 'question' }
)
foreach ($payload in $payloads) {
    $expected = $payload.expected
    $event = @{}
    foreach ($key in $payload.Keys) { if ($key -ne 'expected') { $event[$key] = $payload[$key] } }
    $json = $event | ConvertTo-Json -Depth 10 -Compress
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $hostExe
    $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $root 'Attention.ps1') + '" -DryRun'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.BaseStream.Flush()
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'UTF-8 stdin test timed out' }
        if ($process.ExitCode -ne 0) { throw ('UTF-8 stdin test failed: ' + $errorOutput.Result) }
        $result = $output.Result | ConvertFrom-Json
        if ($result.reason -ne $expected) { throw ('Incorrect event: ' + $expected) }
    } finally { $process.Dispose() }
}
[pscustomobject]@{ utf8StdinEvents = 3; chineseAndEmoji = 'passed'; windowsPowerShellHidden = 'passed'; nativeStdinPipe = 'passed'; windowActions = 'none' } | ConvertTo-Json
