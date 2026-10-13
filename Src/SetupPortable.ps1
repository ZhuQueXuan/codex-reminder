[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path $PSScriptRoot -Parent
try {
    $release = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
    if ($release -lt 528040) { throw '需要 .NET Framework 4.8 或更高版本。请先通过 Windows 更新安装，再运行本入口。' }
    $executable = Join-Path $root 'App\CodexReminder.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw '找不到程序。请先完整解压 ZIP，再双击本入口；不要在压缩包中直接运行。' }
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $result = & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Manage.ps1') -Action Install -Quiet 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($result -join [Environment]::NewLine) }
    Start-Process -FilePath $executable -ArgumentList '--enable' -WindowStyle Hidden -WorkingDirectory $root
    $notice = @'
提醒配置已安装，程序已启动到右下角托盘。

还需你完成信任步骤：
在 Codex 的事件审阅入口，信任带有 CodexAttention 的三个事件。若桌面没有入口，双击“打开事件审阅终端.cmd”，进入后输入 /hooks。

然后回到 Codex，重新开始一个聊天回合。完整步骤见“先看这里.txt”。以后只需双击“启动提醒助手.cmd”。
'@
    $notice += "`r`n`r`n安装目录：" + $root
    [System.Windows.Forms.MessageBox]::Show($notice, 'Codex 提醒助手') | Out-Null
} catch {
    [System.Windows.Forms.MessageBox]::Show(('安装未完成：' + $_.Exception.Message), 'Codex 提醒助手', 'OK', 'Error') | Out-Null
    exit 1
}
