using BLL.DTOs.Backup.OutGoingData;
using BLL.IService;
using DAL.AppDbContext;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Service
{
    // Local one-click SQL Server backup / restore engine for the single-user
    // desktop deployment. Backups are native .bak files stored locally in
    // C:\Sys_Acountant\Backups. The database name is derived from the live
    // EF Core connection string, so no configuration duplication is possible.
    public class BackupService : IBackupService
    {
        private readonly AppDbcontext _context;

        private const string BackupFolder = @"C:\Sys_Acountant\Backups";
        private const string BackupSuffix = "_backup_";
        private const string BackupExtension = ".bak";

        public BackupService(AppDbcontext context)
        {
            _context = context;
        }

        public string GetBackupFolder() => BackupFolder;

        public async Task<BackupResultDto> CreateBackupAsync()
        {
            var result = new BackupResultDto();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var dbName = GetDatabaseName();
                Directory.CreateDirectory(BackupFolder);

                var fileName = $"{dbName}{BackupSuffix}{DateTime.Now:yyyyMMdd_HHmmss}{BackupExtension}";
                var filePath = Path.Combine(BackupFolder, fileName);

                const string sql = @"
BACKUP DATABASE [{0}] TO DISK = @path
WITH FORMAT, MEDIANAME = 'AccountantDbBackups', NAME = 'Full Backup';";

                var started = await ExecuteMasterDbCommandAsync(
                    string.Format(sql, dbName.Replace("]", "]]")),
                    "@path", filePath,
                    timeoutSeconds: 600);

                stopwatch.Stop();

                if (!started)
                    throw new IOException("تعذر إنشاء اتصال بقاعدة البيانات.");

                if (!File.Exists(filePath))
                    throw new IOException("لم يتم إنشاء ملف النسخة الاحتياطية. تأكد من تراخيص مجلد الحفظ.");

                var size = new FileInfo(filePath).Length;

                result.Success = true;
                result.FilePath = filePath;
                result.FileName = fileName;
                result.SizeMb = Math.Round(size / 1024.0 / 1024.0, 2);
                result.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
                result.Message = $"تم إنشاء النسخة الاحتياطية بنجاح ({result.SizeMb:N2} ميجابايت).";
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
                result.Error = ex.Message;
                result.Message = "فشل إنشاء النسخة الاحتياطية.";
            }

            return result;
        }

        public Task<List<BackupFileDto>> GetBackupListAsync()
        {
            var backups = new List<BackupFileDto>();

            if (Directory.Exists(BackupFolder))
            {
                backups = Directory
                    .GetFiles(BackupFolder, "*" + BackupExtension)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select((f, i) => new BackupFileDto
                    {
                        FileName = f.Name,
                        FilePath = f.FullName,
                        SizeMb = Math.Round(f.Length / 1024.0 / 1024.0, 2),
                        CreatedAt = f.LastWriteTime,
                        IsNewest = i == 0
                    })
                    .ToList();
            }

            return Task.FromResult(backups);
        }

        public async Task<BackupResultDto> RestoreBackupAsync(string fileName)
        {
            var result = new BackupResultDto();

            try
            {
                var dbName = GetDatabaseName();
                var filePath = ResolvePathInsideBackupFolder(fileName);

                if (!File.Exists(filePath))
                    throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", fileName);

                if (Path.GetExtension(filePath).Equals(BackupExtension, StringComparison.OrdinalIgnoreCase) == false)
                    throw new InvalidOperationException("الملف المحدد ليس نسخة احتياطية صحيحة (‎.bak).");

                var backupDate = File.GetLastWriteTime(filePath);

                // Exclusive access sequence:
                // 1) kick every user off the database,
                // 2) restore over the live database,
                // 3) hand the database back to normal multi-user mode.
                const string sql = @"
DECLARE @kill varchar(8000) = '';
SELECT @kill = @kill + 'KILL ' + CONVERT(varchar(5), session_id) + ';'
FROM sys.dm_exec_sessions
WHERE database_id = DB_ID('{0}') AND session_id <> @@SPID;
EXEC(@kill);

ALTER DATABASE [{0}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [{0}] FROM DISK = @path WITH REPLACE, RECOVERY;
ALTER DATABASE [{0}] SET MULTI_USER;";

                await ExecuteMasterDbCommandAsync(
                    string.Format(sql, dbName.Replace("]", "]]")),
                    "@path", filePath,
                    timeoutSeconds: 900);

                result.Success = true;
                result.FilePath = filePath;
                result.FileName = Path.GetFileName(filePath);
                result.SizeMb = Math.Round(new FileInfo(filePath).Length / 1024.0 / 1024.0, 2);
                result.Message = $"تم استرجاع قاعدة البيانات من نسخة بتاريخ {backupDate:yyyy-MM-dd HH:mm:ss} بنجاح.";
            }
            catch (Exception ex)
            {
                // If the batch aborts mid-sequence (e.g. RESTORE error), the database
                // may be left stranded in SINGLE_USER. Best-effort recovery so the
                // operator's system stays reachable after a failed restore.
                try
                {
                    var dbNameForRecovery = GetDatabaseName();
                    await ExecuteAdHocMasterCommandAsync(
                        $"ALTER DATABASE [{dbNameForRecovery.Replace("]", "]]")}] SET MULTI_USER;",
                        timeoutSeconds: 120);
                }
                catch { /* recovery is best-effort; surface the original failure */ }

                result.Success = false;
                result.Error = ex.Message;
                result.Message = "فشل استرجاع قاعدة البيانات.";
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private string GetConnectionString()
        {
            var connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("سلسلة الاتصال بقاعدة البيانات غير مضبوطة.");
            return connectionString;
        }

        private string GetDatabaseName()
        {
            var builder = new SqlConnectionStringBuilder(GetConnectionString());
            var dbName = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(dbName))
                throw new InvalidOperationException("لم يتم تحديد اسم قاعدة البيانات في سلسلة الاتصال.");
            return dbName;
        }

        // Executing against master because ALTER/RESTORE/BACKUP statements need
        // a session whose database context is inside `master`, not inside the
        // database being replaced.
        private async Task<bool> ExecuteMasterDbCommandAsync(
            string sql, string parameterName, string parameterValue, int timeoutSeconds)
        {
            var builder = new SqlConnectionStringBuilder(GetConnectionString())
            {
                InitialCatalog = "master"
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            try
            {
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = timeoutSeconds
                };
                command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.NVarChar, 4000)
                {
                    Value = parameterValue
                });
                await command.ExecuteNonQueryAsync();
                return true;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        private async Task<bool> ExecuteAdHocMasterCommandAsync(string sql, int timeoutSeconds)
        {
            var builder = new SqlConnectionStringBuilder(GetConnectionString())
            {
                InitialCatalog = "master"
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            try
            {
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = timeoutSeconds
                };
                await command.ExecuteNonQueryAsync();
                return true;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        private string ResolvePathInsideBackupFolder(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("ملف النسخة الاحتياطية مطلوب.", nameof(fileName));

            var candidate = Path.GetFileName(fileName.Trim());

            if (candidate.Contains(Path.DirectorySeparatorChar) ||
                candidate.Contains(Path.AltDirectorySeparatorChar) ||
                candidate.Contains(".."))
            {
                throw new InvalidOperationException("اسم ملف النسخة الاحتياطية غير صالح.");
            }

            var fullPath = Path.Combine(BackupFolder, candidate);
            var normalizedFolder = Path.GetFullPath(BackupFolder).TrimEnd(Path.DirectorySeparatorChar);

            if (!Path.GetFullPath(fullPath).StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("اسم ملف النسخة الاحتياطية غير صالح.");

            return fullPath;
        }
    }
}
