namespace DAL.Entities
{
    // Audit record for controlled amendments of POSTED maritime supply orders:
    // corrections of weights/prices/additional charges after the order was
    // posted. The amplitude of each correction (stock movements, financial
    // recalculation) is described by the before/after snapshots; the original
    // posted values are never silently overwritten.
    public class InvoiceAmendment
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;

        public DateTime AmendedAtUtc { get; set; } = DateTime.UtcNow;

        // Following the existing convention: controllers resolve the current
        // user id (ClaimTypes.NameIdentifier) and pass it as an optional value.
        public string? AmendedByUserId { get; set; }
        public AppUser? AmendedByUser { get; set; }

        public string Reason { get; set; } = string.Empty;

        // Serialised before/after financial/stock snapshot for the audit trail
        // (old/new totals, item-level weight/price/source changes, charges).
        public string BeforeJson { get; set; } = string.Empty;
        public string AfterJson { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
