namespace DAL.Entities
{
    public class OpeningBalanceEntry
    {
        public int Id { get; set; }
        
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        
        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }
        
        // Positive opening debt amount
        public decimal Amount { get; set; }
        
        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
