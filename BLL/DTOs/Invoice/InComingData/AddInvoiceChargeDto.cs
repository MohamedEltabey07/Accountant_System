using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    public class AddInvoiceChargeDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Required]
        [MaxLength(200, ErrorMessage = "اسم المصروف لا يمكن أن يتجاوز 200 حرف.")]
        public string Description { get; set; } = string.Empty;

        // Optional quantity x unit-price description of the charge.
        [Range(0.001, double.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر.")]
        public decimal? Quantity { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً.")]
        public decimal? UnitPrice { get; set; }

        // Charge total; recalculated on the server from Quantity x UnitPrice whenever
        // both are provided (the posted Amount is never trusted from JavaScript).
        [Range(0.01, double.MaxValue, ErrorMessage = "قيمة المصروف يجب أن تكون أكبر من صفر.")]
        public decimal? Amount { get; set; }

        // true (default) -> billed to maritime company; false -> internal cost only.
        public bool IsBillableToCompany { get; set; } = true;

        [MaxLength(500, ErrorMessage = "الملاحظات لا يمكن أن تتجاوز 500 حرف.")]
        public string? Notes { get; set; }
    }
}
