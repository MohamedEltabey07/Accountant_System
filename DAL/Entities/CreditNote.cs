using DAL.Enums;

namespace DAL.Entities
{
    public class CreditNote
    {
        public int Id { get; set; }
        
        // Formatted unique human-readable number (e.g. CN-0001)
        public string CreditNoteNumber { get; set; } = string.Empty;
        
        public int OriginalInvoiceId { get; set; }
        public Invoice OriginalInvoice { get; set; } = null!;
        
        public CreditNoteType Type { get; set; }
        public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;
        
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PostedAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        // Navigation
        public ICollection<CreditNoteItem> Items { get; set; } = new List<CreditNoteItem>();
        public ICollection<CashTransaction> LinkedCashTransactions { get; set; } = new List<CashTransaction>();
    }
}
