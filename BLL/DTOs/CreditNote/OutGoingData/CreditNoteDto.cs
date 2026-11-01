using DAL.Enums;
using System;
using System.Collections.Generic;

namespace BLL.DTOs.CreditNote.OutGoingData
{
    public class CreditNoteDto
    {
        public int Id { get; set; }
        public string CreditNoteNumber { get; set; } = string.Empty;
        public int OriginalInvoiceId { get; set; }
        public string OriginalInvoiceNumber { get; set; } = string.Empty;
        public CreditNoteType Type { get; set; }
        public CreditNoteStatus Status { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? PostedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public List<CreditNoteItemDto> Items { get; set; } = new List<CreditNoteItemDto>();
    }
}
