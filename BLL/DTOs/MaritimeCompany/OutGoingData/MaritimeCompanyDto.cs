using System;

namespace BLL.DTOs.MaritimeCompany.OutGoingData
{
    public class MaritimeCompanyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Same ledger formula as wholesale customers (opening + supply invoices
        // - credit notes - payments + refunds) for MaritimeSupply invoices.
        public decimal OutstandingBalance { get; set; }
    }
}
