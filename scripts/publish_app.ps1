#Requires -Version 5.1
# =============================================================================
# Accountant_sys — One-click desktop packaging (run on the build machine)
# Publishes a self-contained, folder-based app to C:\Sys_Acountant\App,
# drops a start_app.bat launcher, and creates an Arabic desktop shortcut.
#
# Usage:
#   .\publish_app.ps1              standard publish
#   .\publish_app.ps1 -SkipShortcut  publish without the desktop shortcut
# =============================================================================
param(
    [switch]$SkipShortcut
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = $PSScriptRoot | Split-Path  # repo root (scripts/ lives inside it)
$Project    = Join-Path $RepoRoot 'Accountant_sys\Accountant_sys.csproj'
$OutputDir  = 'C:\Sys_Acountant\App'
$StartBat   = Join-Path $OutputDir 'start_app.bat'
$Shortcut   = $null
$desktopIni = [Environment]::GetFolderPath('Desktop')

Write-Host '=== Accountant_sys packaging started ==='

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project file not found: $Project"
}

# --------------------------------------------------------------- publish -----
Write-Host "Publishing self-contained (win-x64) build to $OutputDir ..."
dotnet publish $Project -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=false `
    -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host 'Publish completed.'

# -------------------------------------------------------- start launcher -----
$batContent = @'
@echo off
title Accountant_sys - المحاسب الذكي
cd /d "%~dp0"
if not exist Accountant_sys.exe (
    echo [X] Accountant_sys.exe not found in this folder. Run publish_app.ps1 again.
    pause
    exit /b 1
)
echo Starting Accountant_sys on http://localhost:5000 ...
start "" http://localhost:5000
Accountant_sys.exe --urls http://localhost:5000
'@
[IO.File]::WriteAllText($StartBat, $batContent, (New-Object Text.ASCIIEncoding))
Write-Host "Launcher created: $StartBat"

# -------------------------------------------------------- desktop shortcut ---
if (-not $SkipShortcut) {
    $Shortcut = Join-Path $desktopIni 'نظام الحسابات - المحاسب الذكي.lnk'
    $wsh = New-Object -ComObject WScript.Shell
    $lnk = $wsh.CreateShortcut($Shortcut)
    $lnk.TargetPath       = $StartBat
    $lnk.WorkingDirectory = $OutputDir
    $lnk.WindowStyle      = 7   # minimized: the browser window is what the operator sees
    $lnk.IconLocation     = (Join-Path $OutputDir 'Accountant_sys.exe') + ',0'
    $lnk.Description       = 'تشغيل نظام المحاسبة المحلي'
    $lnk.Save()
    Write-Host "Desktop shortcut created: $Shortcut"
}

# --------------------------------------------------------------- summary -----
$size = [math]::Round(((Get-ChildItem $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host ''
Write-Host "=== Packaging complete ==="
Write-Host ("App folder:      {0}  ({1} MB)" -f $OutputDir, $size)
Write-Host ("Launcher:        {0}" -f $StartBat)
if ($Shortcut) { Write-Host ("Desktop shortcut: {0}" -f $Shortcut) }
Write-Host 'The operator starts the system through the desktop shortcut.'
