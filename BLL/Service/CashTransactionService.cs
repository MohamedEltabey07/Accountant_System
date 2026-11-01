using AutoMapper;
using BLL.DTOs.Cash.InComingData;
using BLL.DTOs.Cash.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class CashTransactionService : ICashTransactionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public CashTransactionService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<bool> RecordCashMovementAsync(CreateCashMovementDto dto)
        {
            if (dto == null) throw new NullReferenceException("يجب عليك ادخال قيم حتي تتم العمليه");
            if (dto.Amount <= 0) throw new InvalidOperationException("المبلغ يجب ان يكون اكبر من الصفر");
            if (dto.Category == CashCategory.Expense)
            {
                if (dto.ExpenseCategoryId == null) throw new InvalidOperationException("يجب عليك اختيار نوع المصروفات");
                dto.Direction = CashDirection.Out;
            }
            else if (dto.Category == CashCategory.CustomerPayment)
            {
                dto.Direction = CashDirection.In;
            }
            else if (dto.Category == CashCategory.VendorPayment)
            {
                dto.Direction = CashDirection.Out;
            }
            else if (dto.Category == CashCategory.RetailSale)
            {
                dto.Direction = CashDirection.In;
            }
            else if (dto.Category == CashCategory.VendorRefund)
            {
                dto.Direction = CashDirection.In;
            }
            else if (dto.Category == CashCategory.CustomerRefund)
            {
                dto.Direction = CashDirection.Out;
            }
            else if (dto.Category == CashCategory.OtherCashIn)
            {
                dto.Direction = CashDirection.In;
            }
            else if (dto.Category == CashCategory.OtherCashOut)
            {
                dto.Direction = CashDirection.Out;
            }
            var cashTrans = _mapper.Map<CashTransaction>(dto);
            await _unitOfWork.CashTransactionRepo.AddAsync(cashTrans);
            await _unitOfWork.SaveChangesAsync();
            return true;

        }

        public async Task<List<CashTransactionDto>> GetTransactionsAsync(DateTime? from, DateTime? to, CashCategory? category, CashDirection? direction)
        {
            var cashTransList = await _unitOfWork.CashTransactionRepo.GetFilteredTransactionsAsync(from, to, category, direction);
            if (cashTransList == null || !cashTransList.Any()) return new List<CashTransactionDto>();
            var mappedCashTransList = _mapper.Map<List<CashTransactionDto>>(cashTransList);
            return mappedCashTransList;
        }
        public async Task<CashTransactionDto?> GetTransactionByIdAsync(int id)
        {
            var cashTrans = await _unitOfWork.CashTransactionRepo.GetByIdWithDetailsAsync(id);
            if (cashTrans == null) return null;
            var mappedCashTrans = _mapper.Map<CashTransactionDto>(cashTrans);
            return mappedCashTrans;
        }

        public async Task<DailyCashSummaryDto> GetDailyCashSummaryAsync(DateTime date)
        {
            var dailyCashSummary = new DailyCashSummaryDto();
            dailyCashSummary.TotalIn = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(date.Date, date.Date.AddDays(1).AddTicks(-1));
            dailyCashSummary.TotalOut = await _unitOfWork.CashTransactionRepo.GetTotalCashOutAsync(date.Date, date.Date.AddDays(1).AddTicks(-1));
            dailyCashSummary.Date = date;
            return dailyCashSummary;
        }
    }
}
