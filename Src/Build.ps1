[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '本机未找到 .NET Framework 编译器。' }
$app = Join-Path $root 'App'
$assets = Join-Path $root 'Assets'
$scratch = Join-Path $root 'work\tray-build'
New-Item -ItemType Directory -Path $app,$assets,$scratch -Force | Out-Null
$draft = Join-Path $scratch 'CodexReminder-build.exe'
$source = Join-Path $PSScriptRoot 'TrayApp.cs'
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll')
$framework = Split-Path $compiler -Parent
foreach ($assembly in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')) {
    $references += '/reference:' + (Join-Path $framework ('WPF\' + $assembly))
}
$sources = @($source, (Join-Path $PSScriptRoot 'ApprovalObserver.cs'), (Join-Path $PSScriptRoot 'FocusWindow.cs'))
$manifest = Join-Path $PSScriptRoot 'TrayApp.manifest'
$runtimeConfig = Join-Path $root 'Config\TrayApp.exe.config'
& $compiler /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/out:$draft" "/win32manifest:$manifest" @references @sources
if ($LASTEXITCODE -ne 0) { throw '托盘程序编译失败。' }
Copy-Item -LiteralPath $runtimeConfig -Destination ($draft + '.config') -Force
$icon = Join-Path $assets 'CodexReminder.ico'
$iconProcess = Start-Process -FilePath $draft -ArgumentList @('--export-icon', ('"' + $icon + '"')) -WindowStyle Hidden -PassThru -Wait
if ($iconProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $icon)) { throw '图标生成失败。' }
$executable = $OutputPath
if (-not $executable) { $executable = Join-Path $app 'Codex提醒助手.exe' }
& $compiler /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/out:$executable" "/win32icon:$icon" "/win32manifest:$manifest" @references @sources
if ($LASTEXITCODE -ne 0) { throw '最终程序编译失败。' }
Copy-Item -LiteralPath $runtimeConfig -Destination ($executable + '.config') -Force
Get-Item -LiteralPath $executable | Select-Object FullName,Length
