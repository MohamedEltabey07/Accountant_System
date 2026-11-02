using AutoMapper;
using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    // Maritime companies live in the existing Customer table as
    // CustomerType.MaritimeCompany, reusing the whole customer accounting stack
    // (balance, payments with allocations, credit notes, statements) while keeping
    // their screens and queries logically separate from wholesale customers.
    public class MaritimeCompanyService : IMaritimeCompanyService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ICustomerService _customerService;

        public MaritimeCompanyService(IUnitOfWork unitOfWork, IMapper mapper, ICustomerService customerService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _customerService = customerService;
        }

        public async Task<bool> CreateCompanyAsync(CreateMaritimeCompanyDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto), "بيانات الشركة غير مكتملة.");
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException("اسم الشركة البحرية مطلوب.");

            var company = _mapper.Map<Customer>(dto);
            company.Type = CustomerType.MaritimeCompany;
            company.IsActive = true;

            await _unitOfWork.CustomerRepo.AddAsync(company);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateCompanyAsync(UpdateMaritimeCompanyDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto), "بيانات الشركة غير مكتملة.");

            var company = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.Id)
                ?? throw new InvalidOperationException("الشركة المطلوب تعديلها غير موجودة في النظام.");

            if (company.Type != CustomerType.MaritimeCompany)
                throw new InvalidOperationException("لا يمكن تعديل هذا السجل من شاشة الشركات البحرية لأنه ليس شركة بحرية.");

            company.Name = dto.Name.Trim();
            company.PhoneNumber = dto.PhoneNumber;
            company.Address = dto.Address;
            company.IsActive = dto.IsActive;
            company.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ToggleStatusAsync(int id)
            => await _customerService.ToggleStatusAsync(id);

        public async Task<bool> DeleteCompanyAsync(int id)
        {
            // Soft delete (deactivate) — maritime companies keep their financial history.
            var company = await _unitOfWork.CustomerRepo.GetByIdAsync(id)
                ?? throw new InvalidOperationException("الشركة المطلوب حذفها غير موجودة.");

            if (company.Type != CustomerType.MaritimeCompany)
                throw new InvalidOperationException("لا يمكن حذف هذا السجل من شاشة الشركات البحرية.");

            company.IsActive = false;
            company.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<MaritimeCompanyDto>> GetAllCompaniesAsync()
            => await MapWithBalancesAsync(await _unitOfWork.CustomerRepo.GetByTypeAsync(CustomerType.MaritimeCompany));

        public async Task<List<MaritimeCompanyDto>> GetActiveCompaniesAsync()
            => await MapWithBalancesAsync(await _unitOfWork.CustomerRepo.GetActiveByTypeAsync(CustomerType.MaritimeCompany));

        // Status-filtered listing for index pages: operational views default to
        // active only; inactive/all remain available for management.
        public async Task<List<MaritimeCompanyDto>> GetCompaniesByActiveStatusAsync(bool? isActive)
        {
            var companies = (await _unitOfWork.CustomerRepo.GetByTypeAsync(CustomerType.MaritimeCompany))
                ?.Where(c => !isActive.HasValue || c.IsActive == isActive.Value).ToList();
            return await MapWithBalancesAsync(companies ?? new List<Customer>());
        }

        public async Task<MaritimeCompanyDto?> GetCompanyByIdAsync(int id)
        {
            var company = await _unitOfWork.CustomerRepo.GetByIdAsync(id);
            if (company == null || company.Type != CustomerType.MaritimeCompany) return null;

            var dto = _mapper.Map<MaritimeCompanyDto>(company);
            dto.OutstandingBalance = await _customerService.GetCustomerBalanceAsync(company.Id);
            return dto;
        }

        public async Task<decimal> GetCompanyBalanceAsync(int companyId)
        {
            var company = await _unitOfWork.CustomerRepo.GetByIdAsync(companyId)
                ?? throw new InvalidOperationException("الشركة غير موجودة.");

            if (company.Type != CustomerType.MaritimeCompany)
                throw new InvalidOperationException("هذا العميل ليس شركة بحرية.");

            return await _customerService.GetCustomerBalanceAsync(companyId);
        }

        private async Task<List<MaritimeCompanyDto>> MapWithBalancesAsync(List<Customer> companies)
        {
            var result = _mapper.Map<List<MaritimeCompanyDto>>(companies);
            foreach (var dto in result)
                dto.OutstandingBalance = await _customerService.GetCustomerBalanceAsync(dto.Id);
            return result;
        }
    }
}
