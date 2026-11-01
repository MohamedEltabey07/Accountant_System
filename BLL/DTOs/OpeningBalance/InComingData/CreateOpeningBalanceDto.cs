using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.OpeningBalance.InComingData
{
    public class CreateOpeningBalanceDto
    {
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "الرصيد الافتتاحي يجب أن يكون أكبر من صفر")]
        public decimal Amount { get; set; }

        public DateTime EffectiveDate { get; set; } = DateTime.Today;
        public string? Notes { get; set; }
    }
}
