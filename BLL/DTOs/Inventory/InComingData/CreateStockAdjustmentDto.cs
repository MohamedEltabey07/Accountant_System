using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Inventory.InComingData
{
    // Physical count correction: signed quantities (negative = freezer has less,
    // positive = freezer has more). At least one field must be non-zero.
    // Every adjustment requires a documented reason; included quantities must not
    // push the available stock below zero.
    public class CreateStockAdjustmentDto
    {
        [Required(ErrorMessage = "يجب اختيار صنف السمك")]
        public int ProductId { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;

        // Signed adjustment: + for increase, - for decrease. Null/0 = no change.
        [Range(-999999999, 999999999, ErrorMessage = "قيمة تسوية الكراتين غير صحيحة")]
        public decimal? CartonsAdjustment { get; set; }

        [Range(-999999999, 999999999, ErrorMessage = "قيمة تسوية الوزن غير صحيحة")]
        public decimal? WeightKgAdjustment { get; set; }

        [Required(ErrorMessage = "سبب التسوية مطلوب (مثلاً: تسوية الجرد الفعلي)")]
        [MaxLength(500, ErrorMessage = "سبب التسوية يجب أن يكون أقل من 500 حرف")]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "الملاحظات يجب أن تكون أقل من 500 حرف")]
        public string? Notes { get; set; }
    }
}
