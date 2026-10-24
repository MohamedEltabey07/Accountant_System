using DAL.Enums;

namespace DAL.Entities
{
    public class CashTransaction
    {
        public int Id { get; set; }
        
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        
        // Positive physical amount
        public decimal Amount { get; set; }
        
        public CashDirection Direction { get; set; }
        public CashCategory Category { get; set; }
        
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        
        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }
        
        // When cash movement directly originates from an invoice (e.g. Retail Cash Sale)
        public int? SourceInvoiceId { get; set; }
        public Invoice? SourceInvoice { get; set; }
        
        // When cash movement is refund/receipt from a credit note
        public int? CreditNoteId { get; set; }
        public CreditNote? CreditNote { get; set; }
        
        // For operating expenses
        public int? ExpenseCategoryId { get; set; }
        public ExpenseCategory? ExpenseCategory { get; set; }
        
        public string? Notes { get; set; }
        
        // Link to original transaction if this is a reversal/compensating entry
        public int? ReversalOfTransactionId { get; set; }
        public CashTransaction? ReversalOfTransaction { get; set; }
        
        // Associated daily closing record
        public int? DailyClosingId { get; set; }
        public DailyClosing? DailyClosing { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
        public ICollection<CashTransaction> Reversals { get; set; } = new List<CashTransaction>();
    }
}
