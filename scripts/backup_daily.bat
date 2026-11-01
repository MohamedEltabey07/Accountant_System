@echo off
REM =============================================================================
REM Accountant_sys - Daily SQL Server backup (one-click wrapper)
REM Launches backup_daily.ps1 with the exit code passed straight to Task Scheduler.
REM =============================================================================
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0backup_daily.ps1" %*
