using System;
using System.Collections.Generic;

namespace BLL.DTOs.Inventory.OutGoingData
{
    public class InventoryDashboardDto
    {
        // Combined balance of both physical freezers (one logical location).
        public decimal TotalAvailableCartons { get; set; }
        public decimal TotalAvailableKg { get; set; }

        // Incoming/outgoing totals over the selected date range (null = all history).
        public DateTime? IncomingFromDate { get; set; }
        public DateTime? IncomingToDate { get; set; }

        public decimal TotalIncomingKg { get; set; }
        public decimal TotalOutgoingKg { get; set; }
        public decimal TotalIncomingCartons { get; set; }
        public decimal TotalOutgoingCartons { get; set; }

        public int FrozenFishItemsCount { get; set; }
        public int OutOfStockItemsCount { get; set; }

        public List<StockBalanceDto> Balances { get; set; } = new();
        public List<StockBalanceDto> OutOfStockItems { get; set; } = new();
    }
}
