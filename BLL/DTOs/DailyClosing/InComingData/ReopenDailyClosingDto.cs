using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.DailyClosing.InComingData
{
    public class ReopenDailyClosingDto
    {
        [Required]
        public int DailyClosingId { get; set; }

        [Required(ErrorMessage = "سبب إعادة فتح اليومية مطلوب")]
        [StringLength(500, ErrorMessage = "سبب إعادة الفتح لا يتجاوز 500 حرف")]
        public string Reason { get; set; } = string.Empty;
    }
}
