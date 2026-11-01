using System;
using DAL.Enums;

namespace BLL.DTOs.Inventory.OutGoingData
{
    public class StockMovementDto
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;

        public StockMovementType MovementType { get; set; }

        public DateTime MovementDate { get; set; }

        // Positive count for display (direction is MovementType).
        public decimal Cartons { get; set; }

        public decimal? WeightPerCartonKg { get; set; }

        public decimal TotalWeightKg { get; set; }

        public int? SourceInvoiceId { get; set; }

        public string? SourceInvoiceNumber { get; set; }

        public string? Reason { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
