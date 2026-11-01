using System;

namespace BLL.DTOs.DailyClosing.OutGoingData
{
    public class DailyClosingDto
    {
        public int Id { get; set; }
        public DateTime BusinessDate { get; set; }
        public decimal OpeningCashAmount { get; set; }
        public decimal TotalCashIn { get; set; }
        public decimal TotalCashOut { get; set; }
        public decimal ExpectedClosingCashAmount { get; set; }
        public decimal? CountedClosingCashAmount { get; set; }
        public decimal? DifferenceAmount { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
