namespace BLL.DTOs.Backup.OutGoingData
{
    public class BackupFileDto
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public double SizeMb { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsNewest { get; set; }
    }
}
