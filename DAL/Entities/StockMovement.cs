namespace DAL.Entities
{
    using DAL.Enums;

    // Immutable frozen-fish stock ledger. This is the single source of truth for
    // the combined balance of both physical freezers (treated as one logical location).
    // Never hard-delete posted movements; correct them via reversal/adjustment records.
    public class StockMovement
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;

        // Snapshot of the product name at movement creation.
        public string ProductName { get; set; } = string.Empty;

        public StockMovementType MovementType { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;

        // Signed carton count: positive for In, negative for Out, ± for Adjustment.
        // Supports fractional cartons (partial carton withdrawals by weight).
        public decimal Cartons { get; set; }

        // Carton weight in KG as recorded for this movement (when applicable).
        public decimal? WeightPerCartonKg { get; set; }

        // Signed total weight in KG derived from Cartons x WeightPerCartonKg (In),
        // or the actual physically dispatched / adjusted weight (Out / Adjustment).
        public decimal TotalWeightKg { get; set; }

        // Reference to the supply invoice that caused the dispatch/return/reversal.
        public int? SourceInvoiceId { get; set; }
        public Invoice? SourceInvoice { get; set; }

        // Notes or the adjustment reason (required for Adjustment movements).
        public string? Reason { get; set; }

        // Link to the movement reversed by this entry (auditable corrections).
        public int? ReversalOfStockMovementId { get; set; }
        public StockMovement? ReversalOfStockMovement { get; set; }

        public string? CreatedByUserId { get; set; }
        public AppUser? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<StockMovement> Reversals { get; set; } = new List<StockMovement>();
    }
}
