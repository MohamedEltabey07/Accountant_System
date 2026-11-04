namespace DAL.Entities
{
    // Order-level additional charge/expense that belongs to one specific
    // maritime supply order (ice boxes, cartons, packaging, ...).
    // Billable charges are added to the amount billed to the maritime company;
    // internal-only charges are tracked for cost analysis but never billed.
    // This record NEVER creates a cash movement: physical payments are recorded
    // through the regular expense workflow instead.
    public class InvoiceAdditionalCharge
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;

        public string Description { get; set; } = string.Empty;

        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }

        // Charge total used in all calculations (server-recalculated when quantity
        // and unit price are provided, never trusted from the client forms).
        public decimal Amount { get; set; }

        // true  -> included in the supply order total (affects the company balance).
        // false -> internal cost tracking only, not billed to the company.
        public bool IsBillableToCompany { get; set; } = true;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
