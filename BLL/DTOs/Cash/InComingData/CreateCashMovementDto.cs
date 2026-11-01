using System;
using System.ComponentModel.DataAnnotations;
using DAL.Enums;

namespace BLL.DTOs.Cash.InComingData
{
    public class CreateCashMovementDto
    {
        [Required]
        public DateTime TransactionDate { get; set; } = DateTime.Today;

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
        public decimal Amount { get; set; }

        [Required]
        public CashDirection Direction { get; set; }

        [Required]
        public CashCategory Category { get; set; }

        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }
        public int? ExpenseCategoryId { get; set; }

        public string? Notes { get; set; }
    }
}
