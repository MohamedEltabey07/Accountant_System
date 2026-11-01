using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Inventory.InComingData
{
    // Physical withdrawal from the freezers. Weight (KG) is the primary dimension;
    // when Cartons is omitted it is derived as WeightKg / product carton weight
    // (fractional cartons allowed). Available stock is validated server-side.
    public class CreateStockOutDto
    {
        [Required(ErrorMessage = "يجب اختيار صنف السمك")]
        public int ProductId { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "وزن الخروج بالكجم مطلوب")]
        [Range(0.001, double.MaxValue, ErrorMessage = "الوزن يجب أن يكون أكبر من صفر")]
        public decimal WeightKg { get; set; }

        [Range(0.001, double.MaxValue, ErrorMessage = "عدد الكراتين يجب أن يكون أكبر من صفر")]
        public decimal? Cartons { get; set; }

        [Required(ErrorMessage = "سبب الخروج مطلوب في العمليات اليدوية")]
        [MaxLength(500, ErrorMessage = "السبب يجب أن يكون أقل من 500 حرف")]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "الملاحظات يجب أن تكون أقل من 500 حرف")]
        public string? Notes { get; set; }
    }
}
