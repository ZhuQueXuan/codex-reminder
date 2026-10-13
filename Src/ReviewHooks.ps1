$ErrorActionPreference = 'Stop'
$bins = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
$candidate = Get-ChildItem -LiteralPath $bins -Directory | ForEach-Object {
    $exe = Join-Path $_.FullName 'codex.exe'
    if (Test-Path -LiteralPath $exe) { Get-Item -LiteralPath $exe }
} | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $candidate) { Write-Host '未找到随 Codex 安装的终端程序。'; Read-Host '按回车关闭'; exit 1 }
Write-Host '进入 Codex 后，请输入 /hooks，审阅并信任三个 CodexAttention 事件。'
Write-Host '只信任你已经查看的本工具事件；完成后退出，回到桌面应用。'
& $candidate.FullName
