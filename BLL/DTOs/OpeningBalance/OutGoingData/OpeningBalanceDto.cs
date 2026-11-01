using System;

namespace BLL.DTOs.OpeningBalance.OutGoingData
{
    public class OpeningBalanceDto
    {
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public int? VendorId { get; set; }
        public string? VendorName { get; set; }
        public decimal Amount { get; set; }
        public DateTime EffectiveDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
