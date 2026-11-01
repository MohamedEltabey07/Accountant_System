using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.DailyClosing.InComingData
{
    public class CreateDailyClosingDto
    {
        [Required(ErrorMessage = "تاريخ الإقفال مطلوب")]
        public DateTime BusinessDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "المبلغ الفعلي المعدود مطلوب")]
        [Range(0, double.MaxValue, ErrorMessage = "المبلغ المعدود يجب أن يكون صفر أو أكبر")]
        public decimal CountedCashAmount { get; set; }
        [Required(ErrorMessage = "المبلغ الفعلي المعدود مطلوب")]
        [Range(0, double.MaxValue, ErrorMessage = "المبلغ الافتتاحي يجب أن يكون صفر أو أكبر")]
        public decimal OpeningCashAmount { get; set; }

        public string? Notes { get; set; }
    }
}
