using System;

namespace BLL.DTOs.Dashboard.OutGoingData
{
    public class DashboardDto
    {
        public decimal TotalCustomerDebt { get; set; }
        public decimal TotalVendorDebt { get; set; }
        public decimal TodayCashIn { get; set; }
        public decimal TodayCashOut { get; set; }
        public decimal CurrentDrawerCash { get; set; }
        public int OpenInvoicesCount { get; set; }
        public int TodayInvoicesCount { get; set; }
        public bool IsTodayClosed { get; set; }
    }
}
