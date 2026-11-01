using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Backup.InComingData
{
    public class RestoreBackupDto
    {
        [Required]
        public string FileName { get; set; } = string.Empty;
    }
}
