using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.CreditNote.InComingData
{
    public class CreateCreditNoteDraftDto
    {
        [Required]
        public int OriginalInvoiceId { get; set; }

        [Required]
        public DateTime TransactionDate { get; set; } = DateTime.Today;

        [Required]
        public string Reason { get; set; } = string.Empty;

        public List<CreateCreditNoteItemDto> Items { get; set; } = new List<CreateCreditNoteItemDto>();
    }
}
