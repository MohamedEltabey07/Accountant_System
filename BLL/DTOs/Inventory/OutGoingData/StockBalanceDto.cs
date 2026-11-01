using System;

namespace BLL.DTOs.Inventory.OutGoingData
{
    // Ledger-derived balance for one frozen fish item.
    public class StockBalanceDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public decimal TotalInCartons { get; set; }
        public decimal TotalInKg { get; set; }

        public decimal TotalOutCartons { get; set; }
        public decimal TotalOutKg { get; set; }

        public decimal AvailableCartons { get; set; }
        public decimal AvailableKg { get; set; }

        public decimal? DefaultCartonWeightKg { get; set; }
        public decimal? MaritimePricePerKg { get; set; }

        public bool IsOutOfStock => AvailableKg <= 0 && AvailableCartons <= 0;
    }
}
