using AutoMapper;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.DTOs.Vendor.InComingData;
using BLL.IService;
using DAL.Entities;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Service
{
    public class VendorService : IVendorService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public VendorService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }
        public async Task<bool> CreateVendorAsync(CreateVendorDto dto)
        {
            if (dto == null) return false;
            var mapVendor= _mapper.Map<Vendor>(dto);
            await _unitOfWork.Vendors.AddAsync(mapVendor);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteVendorAsync(int id)
        {
            var existVendor = await _unitOfWork.Vendors.GetByIdAsync(id);
            if (existVendor == null) return false;
            existVendor.IsActive = false;
            _unitOfWork.Vendors.Update(existVendor);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<VendorDto>> GetActiveVendorAsync()
        {
            var listOfActiveVendor = await _unitOfWork.Vendors.GetActiveVendorAsync();
            if (listOfActiveVendor == null) return null;
            var mapToVendorDtoList = _mapper.Map<List<VendorDto>>(listOfActiveVendor);
            foreach (var vendor in mapToVendorDtoList)
                vendor.CurrentBalance = await GetVendorBalanceAsync(vendor.Id);
            return mapToVendorDtoList;
        }

        public async Task<List<VendorDto>> GetAllVendorAsync()
        {
            var listOfActiveVendor = await _unitOfWork.Vendors.GetAllAsync();
            if (listOfActiveVendor == null) return null;
            var mapToVendorDtoList = _mapper.Map<List<VendorDto>>(listOfActiveVendor);
            foreach (var vendor in mapToVendorDtoList)
                vendor.CurrentBalance = await GetVendorBalanceAsync(vendor.Id);
            return mapToVendorDtoList;
        }

        public async Task<decimal> GetVendorBalanceAsync(int vendorId)
        {
            var vendorBalance = await _unitOfWork.Vendors.GetVendorBalanceAsync(vendorId);
            return vendorBalance;
        }

        public async Task<VendorDto?> GetVendorByIdAsync(int id)
        {
            var vendor = await _unitOfWork.Vendors.GetByIdAsync(id);
            if (vendor == null) return null;
            var mapToVendorDto = _mapper.Map<VendorDto>(vendor);
            mapToVendorDto.CurrentBalance = await GetVendorBalanceAsync(vendor.Id);
            return mapToVendorDto;
        }

        public async Task<bool> ToggleStatusAsync(int id)
        {
            var vendor = await _unitOfWork.Vendors.GetByIdAsync(id);
            if (vendor == null) return false;
            vendor.IsActive = !vendor.IsActive;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateVendorAsync(UpdateVendorDto dto)
        {
            if (dto == null) return false;
            var existanceVendor = await _unitOfWork.Vendors.GetByIdAsync(dto.Id);
            if (existanceVendor == null) return false;
            _mapper.Map(dto, existanceVendor);
            existanceVendor.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
