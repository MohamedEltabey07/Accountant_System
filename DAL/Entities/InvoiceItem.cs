namespace DAL.Entities
{
    public class InvoiceItem
    {
        public int Id { get; set; }
        
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;
        
        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        
        // Snapshot of the product name at time of invoice creation/posting
        public string ProductName { get; set; } = string.Empty;
        
        // Quantity supporting fractional values (e.g. 2.5 kg)
        public decimal Quantity { get; set; }
        
        // Agreed unit price snapshot
        public decimal UnitPrice { get; set; }
        
        public decimal LineDiscountAmount { get; set; }
        public decimal LineTotal { get; set; }

        // Supply source of this individual item on the invoice. Maritime supply
        // orders only: when true the item is dispatched from the freezer stock
        // (validated against the StockMovement ledger and deducted at posting).
        // When false the item is supplied from outside the freezer and never
        // touches stock. Default false keeps historical rows untouched.
        public bool IsFromFreezer { get; set; }
    }
}
