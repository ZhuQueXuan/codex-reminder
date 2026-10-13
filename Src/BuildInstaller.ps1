[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+(?:\.\d+)?$')]
    [string]$ReleaseVersion = 'v0.2',
    [string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ArchivePath) { $ArchivePath = Join-Path $root ('Releases\Codex提醒助手-' + $ReleaseVersion + '-Windows.zip') }
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$scratch = Join-Path $root ('work\installer-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$payloadHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
$releaseSource = Join-Path $scratch 'ReleaseInfo.cs'
$releaseCode = 'namespace CodexReminderInstaller { static class ReleaseInfo { public const string Version = "' + $ReleaseVersion + '"; public const string PayloadHash = "' + $payloadHash + '"; } }'
[IO.File]::WriteAllText($releaseSource, $releaseCode, (New-Object Text.UTF8Encoding($true)))
$installer = Join-Path $root ('Releases\Codex提醒助手-安装程序-' + $ReleaseVersion + '.exe')
if (Test-Path -LiteralPath $installer) { throw '该版本的安装程序已存在，请选择新的发布版本，保留已有文件。' }
$icon = Join-Path $root 'Assets\CodexReminder.ico'
$references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/out:$installer" "/win32manifest:$PSScriptRoot\Installer.manifest" "/win32icon:$icon" "/resource:$ArchivePath,CodexReminder.Payload" "/resource:$icon,CodexReminder.Icon" @references (Join-Path $PSScriptRoot 'Installer.cs') $releaseSource
if ($LASTEXITCODE -ne 0) { throw '单文件安装程序编译失败。' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($installer + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($installer) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ installer = $installer; bytes = (Get-Item -LiteralPath $installer).Length; embeddedZipSha256 = $payloadHash; sha256 = $hash } | ConvertTo-Json
