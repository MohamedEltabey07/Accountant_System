namespace BLL.DTOs.Backup.OutGoingData
{
    public class BackupResultDto
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public double SizeMb { get; set; }
        public int DurationSeconds { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
}
