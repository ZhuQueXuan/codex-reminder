[CmdletBinding()]
param(
    [ValidateSet('Install','Enable','Disable','Test','Inspect')]
    [string]$Action = 'Install',
    [string]$ConfigDirectory,
    [string]$RuntimeDirectory,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Settings.ps1')
function Show-Result([string]$message) {
    if ($Quiet) { Write-Output $message; return }
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($message, 'Codex 提醒助手') | Out-Null
}
function Set-Enabled([bool]$enabled) {
    Set-ReminderSettings @{ enabled = $enabled }
}
try {
    if ($Action -eq 'Disable') { Set-Enabled $false; Show-Result '已暂停自动弹出。需要时双击“恢复提醒.cmd”。'; exit 0 }
    if ($Action -eq 'Enable') { Set-Enabled $true; Show-Result '已恢复提醒。首次安装后仍需在 Codex 中信任事件配置。'; exit 0 }
    if ($Action -eq 'Inspect') {
        $diagnosis = & (Join-Path $PSScriptRoot 'Attention.ps1') -Diagnose | ConvertFrom-Json
        Show-Result ('能否找到 Codex 窗口：' + $diagnosis.windowFound + "`r`n这一步只检查窗口，不会弹出，也不代表事件已启用。")
        exit 0
    }
    if ($Action -eq 'Test') {
        $testResult = & (Join-Path $PSScriptRoot 'Attention.ps1') -Test | ConvertFrom-Json
        $labels = @{
            'foreground' = 'Codex 已显示在前台。'
            'already-foreground' = 'Codex 已经在前台。请切到其他软件后再测试。'
            'shown-focus-denied' = 'Codex 已尝试置顶显示，但 Windows 没有交出键盘焦点。'
            'window-not-found' = '未找到 Codex 窗口。请先打开 Codex 桌面应用。'
            'focus-denied' = 'Windows 阻止了本次窗口切换。'
        }
        Show-Result ($labels[$testResult.result] + "`r`n这是手动弹出测试，自动事件还需要完成信任步骤。")
        exit 0
    }
    if (-not $ConfigDirectory) {
        if ($env:CODEX_HOME) { $ConfigDirectory = $env:CODEX_HOME }
        else { $ConfigDirectory = Join-Path $env:USERPROFILE '.codex' }
    }
    $configPath = Join-Path $ConfigDirectory 'hooks.json'
    New-Item -ItemType Directory -Path $ConfigDirectory -Force | Out-Null
    $prior = $null
    if (Test-Path -LiteralPath $configPath) {
        $prior = [IO.File]::ReadAllText($configPath)
        $configuration = $prior | ConvertFrom-Json
    } else { $configuration = [pscustomobject]@{ hooks = [pscustomobject]@{} } }
    if (-not $configuration.hooks) { $configuration | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{}) -Force }
    $scriptPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Attention.ps1'
    $command = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $scriptPath + '"'
    $label = 'CodexAttention: 显示 Codex 窗口'
    foreach ($eventName in @('Stop','PermissionRequest','PreToolUse')) {
        $groups = @()
        foreach ($group in @($configuration.hooks.$eventName)) {
            if ($null -eq $group) { continue }
            $remaining = @($group.hooks | Where-Object { $_.statusMessage -ne $label })
            if ($remaining.Count) { $group.hooks = $remaining; $groups += $group }
        }
        $ours = [pscustomobject]@{
            hooks = @([pscustomobject]@{ type = 'command'; command = $command; timeout = 10; statusMessage = $label })
        }
        if ($eventName -eq 'PreToolUse') {
            $ours | Add-Member -NotePropertyName matcher -NotePropertyValue '(^|[._])request_user_input(_async)?$'
        }
        $groups += $ours
        $configuration.hooks | Add-Member -NotePropertyName $eventName -NotePropertyValue $groups -Force
    }
    $serialized = $configuration | ConvertTo-Json -Depth 100
    $runtime = $RuntimeDirectory
    if (-not $runtime) { $runtime = Join-Path $env:LOCALAPPDATA 'CodexAttention' }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    # Detect concurrent changes instead of overwriting them.
    if ($null -ne $prior) {
        if ([IO.File]::ReadAllText($configPath) -cne $prior) { throw '安装过程中事件配置被其他程序修改，请重新安装。' }
        $backup = Join-Path $runtime ('hooks-before-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '.json')
        [IO.File]::WriteAllText($backup, $prior, (New-Object Text.UTF8Encoding($false)))
    } elseif (Test-Path -LiteralPath $configPath) { throw '安装过程中出现了其他事件配置，请重新安装。' }
    [IO.File]::WriteAllText($configPath, $serialized + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    Set-Enabled $true
    Show-Result ("事件配置已写入。`r`n`r`n最后一步：在 Codex 的事件审阅界面信任 CodexAttention 的三个事件。官方明确支持的入口是在 Codex 终端中输入 /hooks。信任后请重新开始一个聊天回合。`r`n`r`n如桌面界面没有提供事件审阅入口，请按【Docs\使用说明.md】中的终端步骤操作。未信任前不会自动弹出。")
} catch {
    Show-Result ('操作未完成：' + $_.Exception.Message)
    exit 1
}
