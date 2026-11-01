using System;

namespace BLL.DTOs.Cash.OutGoingData
{
    public class DailyCashSummaryDto
    {
        public DateTime Date { get; set; }
        public decimal TotalIn { get; set; }
        public decimal TotalOut { get; set; }
        public decimal NetCashMovement => TotalIn - TotalOut;
    }
}
