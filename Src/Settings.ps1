function Get-ReminderProjectKey {
    $root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\').ToUpperInvariant()
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($root))).Replace('-', '').Substring(0,16).ToLowerInvariant() }
    finally { $hash.Dispose() }
}

function Test-ReminderTrayRunning {
    $handle = $null
    try {
        $handle = [Threading.Mutex]::OpenExisting('Local\CodexAttention.Tray.' + (Get-ReminderProjectKey))
        return $true
    } catch [Threading.WaitHandleCannotBeOpenedException] { return $false }
    finally { if ($handle) { $handle.Dispose() } }
}

function Set-ReminderSettings([hashtable]$Changes) {
    $path = Join-Path (Split-Path $PSScriptRoot -Parent) 'Config\settings.json'
    $gate = New-Object Threading.Mutex($false, ('Local\CodexAttention.Settings.' + (Get-ReminderProjectKey)))
    $locked = $false
    try {
        try { $locked = $gate.WaitOne(3000) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw '配置正在被其他操作使用，请稍后再试。' }
        $data = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($key in $Changes.Keys) { $data | Add-Member -NotePropertyName $key -NotePropertyValue $Changes[$key] -Force }
        $pending = $path + '.pending'
        [IO.File]::WriteAllText($pending, ($data | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
        [IO.File]::Replace($pending, $path, [NullString]::Value)
    } finally {
        if ($locked) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
}
