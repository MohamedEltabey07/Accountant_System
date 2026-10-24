using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.IRepos
{
    public interface IVendorRepo : IGenericRepo<Vendor>
    {
        public Task<List<Vendor>> GetActiveVendorAsync();
        public Task<Vendor?> GetVendorWithFinancialDetailsAsync(int vendorId);
        public Task<bool> HasFinancialHistoryAsync(int vendorId);
        public Task<decimal> GetVendorBalanceAsync(int vendorId);
    }
}
