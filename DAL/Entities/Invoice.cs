using DAL.Enums;

namespace DAL.Entities
{
    public class Invoice
    {
        public int Id { get; set; }
        
        // Formatted human-readable number (e.g. PUR-0001, SAL-0001, RET-0001)
        public string InvoiceNumber { get; set; } = string.Empty;
        
        public InvoiceType Type { get; set; }
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
        
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        
        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        // External order number issued by the maritime company (MaritimeSupply only).
        // Internal InvoiceNumber (MAR-####) is generated separately by the system.
        public string? ExternalOrderNumber { get; set; }

        // Supply/order date received from the maritime company (MaritimeSupply only).
        public DateTime? SupplyOrderDate { get; set; }

        public decimal SubTotal { get; set; }
        public decimal ItemDiscountTotal { get; set; }
        public decimal InvoiceDiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PostedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }

        // Navigation
        public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
        public ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();
        public ICollection<CashTransaction> LinkedCashTransactions { get; set; } = new List<CashTransaction>();
        public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    }
}
