[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+(?:\.\d+)?$')]
    [string]$ReleaseVersion = 'v0.2'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tag = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$staging = Join-Path $root ('work\package-' + $tag + '\Codex提醒助手')
$releases = Join-Path $root 'Releases'
New-Item -ItemType Directory -Path $staging,$releases -Force | Out-Null
# Explicit allowlist: never distribute user state, logs, trust or developer history.
$files = @(
    'Attention.ps1',
    'App\Codex提醒助手.exe',
    'App\Codex提醒助手.exe.config',
    'Src\Attention.ps1',
    'Src\Settings.ps1',
    'Src\FocusWindow.cs',
    'Src\Manage.ps1',
    'Src\ReviewHooks.ps1'
)
foreach ($relative in $files) {
    $targetRelative = $relative.Replace('Codex提醒助手.exe', 'CodexReminder.exe')
    $target = Join-Path $staging $targetRelative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
$utf8 = New-Object Text.UTF8Encoding($true)
$setup = [IO.File]::ReadAllText((Join-Path $root 'Src\SetupPortable.ps1'))
[IO.File]::WriteAllText((Join-Path $staging 'Src\Setup.ps1'), $setup, $utf8)
New-Item -ItemType Directory -Path (Join-Path $staging 'Config'),(Join-Path $staging 'Docs') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $staging 'Config\settings.json'), '{"enabled":false,"sound":true,"require_tray":true,"notify_completed":true,"notify_approval":true,"notify_question":true}', (New-Object Text.UTF8Encoding($false)))
$guide = [IO.File]::ReadAllText((Join-Path $root 'Docs\分享版使用说明.md'))
[IO.File]::WriteAllText((Join-Path $staging 'Docs\使用说明.md'), $guide, $utf8)
[IO.File]::WriteAllText((Join-Path $staging '先看这里.txt'), $guide.Replace('# ', '').Replace('## ', '').Replace('**', '').Replace('`', ''), $utf8)
$first = @'
@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Src\Setup.ps1"
'@
$start = @'
@echo off
start "" "%~dp0App\CodexReminder.exe"
'@
$review = @'
@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Src\ReviewHooks.ps1"
'@
[IO.File]::WriteAllText((Join-Path $staging '首次安装并启动.cmd'), $first.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $staging '启动提醒助手.cmd'), $start.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $staging '打开事件审阅终端.cmd'), $review.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
$name = 'Codex提醒助手-' + $ReleaseVersion + '-Windows'
$archive = Join-Path $releases ($name + '.zip')
if (Test-Path -LiteralPath $archive) { $archive = Join-Path $releases ($name + '-' + $tag + '.zip') }
Compress-Archive -LiteralPath $staging -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($archive + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ archive = $archive; bytes = (Get-Item -LiteralPath $archive).Length; sha256 = $hash; staging = $staging } | ConvertTo-Json
