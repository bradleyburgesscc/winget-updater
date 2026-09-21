#requires -version 5.1
<#
.SYNOPSIS
    Publishes winget-updater as a single self-contained NativeAOT executable and copies
    it to .\dist\winget-updater.exe.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$distDir = Join-Path $root "dist"

Write-Host "Publishing winget-updater ($Configuration, win-x64, NativeAOT)..." -ForegroundColor Cyan

dotnet publish (Join-Path $root "WingetUpdater.csproj") `
    -c $Configuration `
    -r win-x64 `
    -o $distDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $distDir "winget-updater.exe"
if (-not (Test-Path $exe)) {
    throw "Expected output not found: $exe"
}

$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host "Published: $exe ($sizeMb MB)" -ForegroundColor Green
