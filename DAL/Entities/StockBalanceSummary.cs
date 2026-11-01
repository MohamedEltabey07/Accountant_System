namespace DAL.Entities
{
    // Aggregated ledger-derived balance for one frozen fish product.
    public class StockBalanceSummary
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;

        public decimal TotalInCartons { get; set; }
        public decimal TotalInKg { get; set; }

        public decimal TotalOutCartons { get; set; }
        public decimal TotalOutKg { get; set; }

        public decimal AvailableCartons { get; set; }
        public decimal AvailableKg { get; set; }

        public decimal? DefaultCartonWeightKg { get; set; }
        public decimal? MaritimePricePerKg { get; set; }
    }
}
