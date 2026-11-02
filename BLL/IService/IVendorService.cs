using BLL.DTOs.Customer.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Vendor.InComingData;
using BLL.DTOs.Vendor.OutGoingData;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.IService
{
    public interface IVendorService
    {
        public Task<List<VendorDto>> GetAllVendorAsync();
        public Task<List<VendorDto>> GetActiveVendorAsync();
        // isActive: true = active only (default operational view),
        // false = inactive only, null = all records (management view).
        public Task<List<VendorDto>> GetVendorsByActiveStatusAsync(bool? isActive);
        public Task<VendorDto?> GetVendorByIdAsync(int id);
        public Task<decimal> GetVendorBalanceAsync(int vendorId);
        public Task<bool> CreateVendorAsync(CreateVendorDto dto);
        public Task<bool> UpdateVendorAsync(UpdateVendorDto dto);
        public Task<bool> ToggleStatusAsync(int id);
        public Task<bool> DeleteVendorAsync(int id);
    }
}
