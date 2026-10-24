namespace DAL.Entities
{
    public class Vendor
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
        public ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();
        public ICollection<OpeningBalanceEntry> OpeningBalanceEntries { get; set; } = new List<OpeningBalanceEntry>();
    }
}
