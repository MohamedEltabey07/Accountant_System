using System;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Inventory.InComingData
{
    // Stock receipt: cartons entering the freezers. When WeightPerCartonKg is omitted
    // the product's DefaultCartonWeightKg is used. TotalWeightKg = Cartons x (resolved weight).
    public class CreateStockInDto
    {
        [Required(ErrorMessage = "يجب اختيار صنف السمك")]
        public int ProductId { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "عدد الكراتين مطلوب")]
        [Range(0.001, double.MaxValue, ErrorMessage = "عدد الكراتين يجب أن يكون أكبر من صفر")]
        public decimal Cartons { get; set; }

        [Range(0.001, double.MaxValue, ErrorMessage = "وزن الكرتونة يجب أن يكون أكبر من صفر")]
        public decimal? WeightPerCartonKg { get; set; }

        [MaxLength(500, ErrorMessage = "الملاحظات يجب أن تكون أقل من 500 حرف")]
        public string? Notes { get; set; }
    }
}
