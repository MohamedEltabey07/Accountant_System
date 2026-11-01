using BLL.DTOs.Backup.OutGoingData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IBackupService
    {
        Task<BackupResultDto> CreateBackupAsync();
        Task<List<BackupFileDto>> GetBackupListAsync();
        Task<BackupResultDto> RestoreBackupAsync(string fileName);
        string GetBackupFolder();
    }
}
