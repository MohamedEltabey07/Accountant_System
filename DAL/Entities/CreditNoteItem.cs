namespace DAL.Entities
{
    public class CreditNoteItem
    {
        public int Id { get; set; }
        
        public int CreditNoteId { get; set; }
        public CreditNote CreditNote { get; set; } = null!;
        
        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        
        // Snapshot of the product name at return time
        public string ProductName { get; set; } = string.Empty;
        
        // Returned quantity supporting fractional values (e.g. 1.25 kg)
        public decimal Quantity { get; set; }
        
        // Snapshot unit price
        public decimal UnitPrice { get; set; }
        
        public decimal LineTotal { get; set; }
    }
}
