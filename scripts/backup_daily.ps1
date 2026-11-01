#Requires -Version 5.1
# =============================================================================
# Accountant_sys — Daily Database Backup
# Creates a full native SQL Server .bak backup of the accounting database into
# C:\Sys_Acountant\Backups and purges backups older than 30 days.
# Registered as a Windows Scheduled Task by install_backup_task.ps1.
# Exit codes: 0 = success, 1 = failure.
# =============================================================================
param(
    [int]$PurgeOlderThanDays = 30
)

$ErrorActionPreference = 'Stop'

# -- Locations ----------------------------------------------------------------
$BackupDir   = 'C:\Sys_Acountant\Backups'
$LogFile     = Join-Path $BackupDir 'backup_log.txt'
$ConfigPaths = @(
    'C:\Sys_Acountant\App\appsettings.json',
    (Join-Path $PSScriptRoot '..\Accountant_sys\appsettings.json')
)

# -- Read the connection string from the deployed appsettings.json ------------
function Get-ConnectionString {
    foreach ($configPath in $ConfigPaths) {
        if (Test-Path -LiteralPath $configPath) {
            try {
                $json = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
                $conn = $json.ConnectionStrings.DefaultConnection
                if ($conn -and $conn.Trim().Length -gt 0) { return $conn.Trim() }
            } catch {
                # Fall through to the next candidate path.
            }
        }
    }
    # Same defaults as the deployed appsettings.json.
    return 'Data Source=ELTABEY\SQLEXPRESS;Initial Catalog=AccountantDb;Integrated Security=True;Connect Timeout=30;Encrypt=True;Trust Server Certificate=True;'
}

function Write-Log([string]$level, [string]$message) {
    $line = "{0} [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $level, $message
    Write-Output $line
    try {
        $line | Out-File -FilePath $LogFile -Append -Encoding UTF8
    } catch { # A folder-permission problem here must not crash the run.
    }
}

# -- Backup directory (created on first run if missing) -------------------------
try {
    if (-not (Test-Path -LiteralPath $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    }
} catch {
    Write-Host "FATAL: cannot create backup folder $BackupDir : $($_.Exception.Message)"
    exit 1
}

Write-Log 'INFO' '=== Daily backup run started ==='

try {
    $connectionString = Get-ConnectionString

    # Database name comes from Initial Catalog / Database= in the connection string.
    if ($connectionString -match '(?i)(initial\s+catalog|database)\s*=\s*([^;]+)') {
        $DbName = $Matches[2].Trim()
    } else {
        throw 'Cannot determine the database name from the connection string.'
    }
} catch {
    Write-Log 'ERROR' "Connection string resolution failed: $($_.Exception.Message)"
    exit 1
}

$stamp    = Get-Date -Format 'yyyyMMdd_HHmmss'
$fileName = '{0}_backup_{1}.bak' -f $DbName, $stamp
$filePath = Join-Path $BackupDir $fileName

try {
    # Ensure System.Data.SqlClient is loaded (built into Windows PowerShell 5.1).
    Add-Type -AssemblyName System.Data | Out-Null

    # System.Data.SqlClient (framework provider) rejects newer Microsoft.Data.SqlClient
    # keywords such as 'Trust Server Certificate'; 'Encrypt' would also hard-require a
    # certificate chain that a local SQLEXPRESS instance does not have. Drop both.
    $legacySafe = $connectionString -replace '(?i)trust\s*server\s*certificate\s*=\s*[^;]+;?', '' -replace '(?i)encrypt\s*=\s*[^;]+;?', ''

    # Execute against [master]: BACKUP needs a session not context-bound to the target db.
    $masterBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($legacySafe)
    $masterBuilder['Initial Catalog'] = 'master'

    $sql = @"
BACKUP DATABASE [$DbName] TO DISK = @path
WITH FORMAT, MEDIANAME = 'AccountantDbBackups', NAME = 'Full Backup';
"@

    $connection = New-Object System.Data.SqlClient.SqlConnection($masterBuilder.ConnectionString)
    $command    = $connection.CreateCommand()
    $command.CommandText         = $sql
    $command.CommandTimeout      = 600  # 10 min ceiling for a small local store db
    [void]$command.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 4000)
    $command.Parameters['@path'].Value = $filePath

    $connection.Open()
    try {
        # GetAffectedRows etc. are irrelevant; a successful ExecuteNonQuery is our success signal.
        [void]$command.ExecuteNonQuery()
    } finally {
        $connection.Close()
        $connection.Dispose()
    }

    if (-not (Test-Path -LiteralPath $filePath)) {
        throw 'sqlcmd backup finished but no .bak file appeared on disk.'
    }

    $sizeMb = [math]::Round(((Get-Item -LiteralPath $filePath).Length) / 1MB, 2)
    Write-Log 'INFO' ("Backup created successfully: {0} ({1} MB)" -f $fileName, $sizeMb)
} catch {
    Write-Log 'ERROR' ("Backup failed: {0}" -f $_.Exception.Message)
    if (Test-Path -LiteralPath $filePath) {
        # A half-written/partial .bak must not linger where restore code can find it.
        Remove-Item -LiteralPath $filePath -Force -ErrorAction SilentlyContinue | Out-Null
    }
    exit 1
}

# -- Purge backups older than the retention window -----------------------------
try {
    $cutoff = (Get-Date).AddDays(-$PurgeOlderThanDays)
    $stale  = Get-ChildItem -LiteralPath $BackupDir -Filter '*.bak' -File |
              Where-Object { $_.LastWriteTime -lt $cutoff }

    foreach ($old in $stale) {
        Remove-Item -LiteralPath $old.FullName -Force
        Write-Log 'INFO' ("Purged stale backup older than {0} days: {1}" -f $PurgeOlderThanDays, $old.Name)
    }
    Write-Log 'INFO' ("Retention scan complete — {0} file(s) older than {1} day(s) removed." -f $stale.Count, $PurgeOlderThanDays)
} catch {
    Write-Log 'WARNING' ("Purge step failed (the backup itself succeeded): {0}" -f $_.Exception.Message)
}

Write-Log 'INFO' '=== Daily backup run finished successfully ==='
exit 0
