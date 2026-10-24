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
        public Task<VendorDto?> GetVendorByIdAsync(int id);
        public Task<decimal> GetVendorBalanceAsync(int vendorId);
        public Task<bool> CreateVendorAsync(CreateVendorDto dto);
        public Task<bool> UpdateVendorAsync(UpdateVendorDto dto);
        public Task<bool> ToggleStatusAsync(int id);
        public Task<bool> DeleteVendorAsync(int id);
    }
}
