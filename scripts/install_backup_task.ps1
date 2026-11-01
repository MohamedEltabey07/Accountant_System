#Requires -Version 5.1
# =============================================================================
# Accountant_sys — One-time installer (run as Administrator)
# Registers a Windows Scheduled Task that runs backup_daily.bat every day at
# 23:00. Scripts are copied to C:\Sys_Acountant\Scripts so the task keeps
# working even if the source folder moves.
#
# Usage:
#   .\install_backup_task.ps1                Install (daily 23:00 default)
#   .\install_backup_task.ps1 -Time 21:30    Install with a different time
#   .\install_backup_task.ps1 -RunNow        Install and run one backup now
#   .\install_backup_task.ps1 -Remove        Delete the scheduled task
# =============================================================================
param(
    [string]$Time = '23:00',
    [switch]$RunNow,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$TaskName     = 'AccountantSys_DailyBackup'
$StableFolder = 'C:\Sys_Acountant\Scripts'
$StableBat    = Join-Path $StableFolder 'backup_daily.bat'

$taskInstalled = $false

try {
    # ---------------------------------------------------------------- remove --
    if ($Remove) {
        Write-Host "Removing scheduled task '$TaskName'..."
        schtasks /Delete /TN $TaskName /F 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Host 'Task deleted successfully.' }
        else { Write-Host 'Task not found (or already removed).' }
        exit 0
    }

    # ---------------------------------------------------------------- copy ----
    if (-not (Test-Path -LiteralPath $StableFolder)) {
        New-Item -ItemType Directory -Path $StableFolder -Force | Out-Null
    }

    if (-not (Test-Path (Join-Path $PSScriptRoot 'backup_daily.ps1'))) {
        throw 'backup_daily.ps1/.bat must be placed next to this installer.'
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backup_daily.ps1') -Destination $StableFolder -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backup_daily.bat') -Destination $StableFolder -Force
    Write-Host "Scripts copied to stable folder: $StableFolder"

    # ---------------------------------------------------------------- create --
    # Run under the current interactive user (same identity SQL Server already
    # trusts for Windows auth) with the highest privileges available.
    $action   = New-ScheduledTaskAction -Execute $StableBat
    $trigger  = New-ScheduledTaskTrigger -Daily -At $Time
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
                                             -DontStopIfGoingOnBatteries `
                                             -StartWhenAvailable `
                                             -ExecutionTimeLimit (New-TimeSpan -Hours 1)

    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest

    Register-ScheduledTask -TaskName $TaskName `
                           -Action $action `
                           -Trigger $trigger `
                           -Settings $settings `
                           -Principal $principal `
                           -Description 'Accountant_sys: full SQL Server .bak backup daily to C:\Sys_Acountant\Backups with 30-day retention.' | Out-Null

    $taskInstalled = $true
    Write-Host "Scheduled task '$TaskName' registered — runs daily at $Time."

    # ------------------------------------------------------------- run now ----
    if ($RunNow) {
        Write-Host 'Running one backup now...'
        & $StableBat
        if ($LASTEXITCODE -ne 0) {
            throw 'Scheduled backup script returned failure — see C:\Sys_Acountant\Backups\backup_log.txt'
        }
        Write-Host 'Backup completed successfully. Check C:\Sys_Acountant\Backups'
    }

    Write-Host ''
    Write-Host 'DONE. Backups land in C:\Sys_Acountant\Backups; history in C:\Sys_Acountant\Backups\backup_log.txt.'
} catch {
    Write-Host ("FAILED: {0}" -f $_.Exception.Message)
    # Partial state must never leave the machine half-scheduled.
    if ($taskInstalled) {
        try {
            schtasks /Delete /TN $TaskName /F | Out-Null
            Write-Host 'Rolled back the scheduled task.'
        } catch { }
    }
    exit 1
}
