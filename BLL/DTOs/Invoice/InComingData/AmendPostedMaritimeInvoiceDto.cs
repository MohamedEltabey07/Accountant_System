using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    // Controlled amendment of a POSTED maritime supply order. The original posted
    // document is preserved as history through the InvoiceAmendment audit record
    // and compensating stock movements; payments/allocations are never touched.
    public class AmendPostedMaritimeInvoiceDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Required]
        [MaxLength(500, ErrorMessage = "سبب التصحيح مطلوب ولا يمكن أن يتجاوز 500 حرف.")]
        public string Reason { get; set; } = string.Empty;

        // Optional correction of the maritime order meta data (revalidated for uniqueness).
        public string? ExternalOrderNumber { get; set; }
        public DateTime? SupplyOrderDate { get; set; }

        // Existing items to Modify (weights/prices/discount/freezer sourcing).
        public List<UpdateInvoiceItemDto> UpdatedItems { get; set; } = new List<UpdateInvoiceItemDto>();

        // Existing item ids to remove.
        public List<int> RemovedItemIds { get; set; } = new List<int>();

        // New items (AddInvoiceItemDto; the InvoiceId field is forced server-side).
        public List<AddInvoiceItemDto> NewItems { get; set; } = new List<AddInvoiceItemDto>();

        // Additional charges upsert: ChargeId == 0 (or unknown) -> new charge.
        public List<UpdateInvoiceChargeDto> Charges { get; set; } = new List<UpdateInvoiceChargeDto>();

        // Charge ids to remove.
        public List<int> RemovedChargeIds { get; set; } = new List<int>();
    }
}
