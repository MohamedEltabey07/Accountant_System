using AutoMapper;
using BLL.DTOs.OpeningBalance.InComingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class OpeningBalanceService : IOpeningBalanceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public OpeningBalanceService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<bool> SetCustomerOpeningBalanceAsync(CreateOpeningBalanceDto dto)
        {
            if (dto == null) throw new ArgumentNullException("يجب عليك ادخال القيم");
            if (dto.Amount <= 0) throw new InvalidOperationException("يجب ان يكون المبلغ المالي اكبر من الصفر");
            var checkCstExist = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.CustomerId.Value);
            if (checkCstExist == null) throw new NullReferenceException("هذا العميل غير موجود");
            if (!checkCstExist.IsActive) throw new InvalidOperationException($"العميل ({checkCstExist.Name}) غير نشط، لا يمكن إنشاء رصيد افتتاحي له.");
            var checkCstHasOpeningBalance = await _unitOfWork.OpeningBalanceRepo.GetByCustomerIdAsync(dto.CustomerId.Value);
            if (checkCstHasOpeningBalance != null) throw new InvalidOperationException("هذا العميل لديه حساب مفتوح بالفعل");
            var mappedOpeningBalance = _mapper.Map<OpeningBalanceEntry>(dto);
            mappedOpeningBalance.CreatedAt = DateTime.UtcNow;
            mappedOpeningBalance.EffectiveDate = dto.EffectiveDate;
            await _unitOfWork.OpeningBalanceRepo.AddAsync(mappedOpeningBalance);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetVendorOpeningBalanceAsync(CreateOpeningBalanceDto dto)
        {
            if (dto == null) throw new ArgumentNullException("يجب عليك ادخال القيم");
            if (dto.Amount <= 0) throw new InvalidOperationException("يجب ان يكون المبلغ المالي اكبر من الصفر");
            var checkVdExist = await _unitOfWork.Vendors.GetByIdAsync(dto.VendorId.Value);
            if (checkVdExist == null) throw new NullReferenceException("هذا العميل غير موجود");
            if (!checkVdExist.IsActive) throw new InvalidOperationException($"المورد ({checkVdExist.Name}) غير نشط، لا يمكن إنشاء رصيد افتتاحي له.");
            var checkVdHasOpeningBalance = await _unitOfWork.OpeningBalanceRepo.GetByVendorIdAsync(dto.VendorId.Value);
            if (checkVdHasOpeningBalance != null) throw new InvalidOperationException("هذا العميل لديه حساب مفتوح بالفعل");
            var mappedOpeningBalance = _mapper.Map<OpeningBalanceEntry>(dto);
            mappedOpeningBalance.CreatedAt = DateTime.UtcNow;
            mappedOpeningBalance.EffectiveDate = dto.EffectiveDate;
            await _unitOfWork.OpeningBalanceRepo.AddAsync(mappedOpeningBalance);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<OpeningBalanceDto>> GetAllOpeningBalancesAsync()
        {
            var allOpBalance = await _unitOfWork.OpeningBalanceRepo.GetAllWithPartiesAsync();
            if (allOpBalance == null || !allOpBalance.Any()) return new List<OpeningBalanceDto>();
            var mappedOpBalanceList = _mapper.Map<List<OpeningBalanceDto>>(allOpBalance);
            return mappedOpBalanceList;
        }
    }
}
