namespace DAL.Entities
{
    public class PaymentAllocation
    {
        public int Id { get; set; }
        
        public int CashTransactionId { get; set; }
        public CashTransaction CashTransaction { get; set; } = null!;
        
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;
        
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
