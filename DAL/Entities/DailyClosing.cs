namespace DAL.Entities
{
    public class DailyClosing
    {
        public int Id { get; set; }
        
        // Business day date (Unique index - one closing record per day)
        public DateTime BusinessDate { get; set; }
        
        public decimal OpeningCashAmount { get; set; }
        public decimal ExpectedClosingCashAmount { get; set; }
        public decimal? CountedClosingCashAmount { get; set; }
        public decimal? DifferenceAmount { get; set; }
        
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<CashTransaction> Transactions { get; set; } = new List<CashTransaction>();
    }
}
