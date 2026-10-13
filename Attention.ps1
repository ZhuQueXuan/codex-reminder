# Stable Codex hook entry. Keep its registered path unchanged.
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Diagnose,
    [switch]$DryRun,
    [switch]$ApprovalSignal,
    [string]$PayloadFile,
    [string]$RuntimeDirectory
)
& (Join-Path $PSScriptRoot 'Src\Attention.ps1') @PSBoundParameters
