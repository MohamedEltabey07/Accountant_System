namespace DAL.Entities
{
    using DAL.Enums;

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal PurchasePrice { get; set; }
        public decimal WholesalePrice { get; set; }
        public decimal RetailPrice { get; set; }
        public bool IsActive { get; set; } = true;

        // Only FrozenFish products participate in the freezer stock ledger.
        // Existing rows default to RegularProduct via migration.
        public ProductCategory Category { get; set; } = ProductCategory.RegularProduct;

        // Reference carton weight in KG for frozen fish items (drives carton count math).
        public decimal? DefaultCartonWeightKg { get; set; }

        // Optional default selling price per KG for maritime supply orders.
        public decimal? MaritimePricePerKg { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
        public ICollection<CreditNoteItem> CreditNoteItems { get; set; } = new List<CreditNoteItem>();
        public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    }
}
