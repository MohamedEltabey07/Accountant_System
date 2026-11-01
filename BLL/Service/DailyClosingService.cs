using AutoMapper;
using BLL.DTOs.DailyClosing.InComingData;
using BLL.DTOs.DailyClosing.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class DailyClosingService : IDailyClosingService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public DailyClosingService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<DailyClosingDto?> GetClosingByDateAsync(DateTime businessDate)
        {
            var closingDay = await _unitOfWork.DailyClosingRepo.GetByDateAsync(businessDate);
            if (closingDay == null) return null;
            var mappedDailyClosing = _mapper.Map<DailyClosingDto>(closingDay);
            return mappedDailyClosing;
        }

        public async Task<DailyClosingDto> CalculatePreviewClosingAsync(DateTime businessDate)
        {
            var dailyClosingDto = new DailyClosingDto();
            dailyClosingDto.TotalCashIn = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(businessDate.Date , businessDate.AddDays(1).Date);
            dailyClosingDto.TotalCashOut = await _unitOfWork.CashTransactionRepo.GetTotalCashOutAsync(businessDate.Date, businessDate.AddDays(1).Date);
            dailyClosingDto.ExpectedClosingCashAmount = (dailyClosingDto.TotalCashIn + dailyClosingDto.OpeningCashAmount) - dailyClosingDto.TotalCashOut;
            return dailyClosingDto;
        }

        public async Task<bool> PerformDailyClosingAsync(CreateDailyClosingDto dto)
        {
            var checkIsClosedDay = await IsDateClosedAsync(dto.BusinessDate);
            if (checkIsClosedDay) throw new InvalidOperationException("هذا اليوم تم اغلاقه بالفعل");
            var moneyIn = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(dto.BusinessDate.Date
                , dto.BusinessDate.AddDays(1).Date);
            var moneyOut = await _unitOfWork.CashTransactionRepo.GetTotalCashOutAsync(dto.BusinessDate.Date
                , dto.BusinessDate.AddDays(1).Date);
            var allCashTrans = moneyIn + moneyOut;
            var expectedClosingCashAmount = (dto.OpeningCashAmount + moneyIn) - moneyOut;
            var differenceAmount = (dto.CountedCashAmount ?? 0m) - expectedClosingCashAmount;
            var closingDay = await _unitOfWork.DailyClosingRepo.GetByDateAsync(dto.BusinessDate);
            if (closingDay == null)
            {
                var dailyClosing = new DailyClosing
                {
                    BusinessDate = dto.BusinessDate,
                    CountedClosingCashAmount = dto.CountedCashAmount ?? 0m,
                    OpeningCashAmount = dto.OpeningCashAmount,
                    ExpectedClosingCashAmount = expectedClosingCashAmount,
                    DifferenceAmount = differenceAmount,
                    CreatedAt = DateTime.UtcNow,
                    ClosedAt = DateTime.UtcNow,
                    IsClosed = true,
                    Notes = dto.Notes
                };
                await _unitOfWork.DailyClosingRepo.AddAsync(dailyClosing);
                await _unitOfWork.SaveChangesAsync();
                return true;
            }

            // Re-fetch as a change-tracked instance (handles a previously reopened
            // closing already tracked by the current unit of work) and save.
            // _dbSet.Update on a second detached instance would throw a
            // duplicate-tracking exception in that case.
            var trackedClosing = await _unitOfWork.DailyClosingRepo.GetByIdAsync(closingDay.Id);
            if (trackedClosing == null)
            {
                trackedClosing = closingDay;
                _unitOfWork.DailyClosingRepo.Update(trackedClosing);
            }
            trackedClosing.OpeningCashAmount = dto.OpeningCashAmount;
            trackedClosing.ExpectedClosingCashAmount = expectedClosingCashAmount;
            trackedClosing.CountedClosingCashAmount = dto.CountedCashAmount ?? 0m;
            trackedClosing.DifferenceAmount = differenceAmount;
            trackedClosing.IsClosed = true;
            trackedClosing.ClosedAt = DateTime.UtcNow;
            trackedClosing.Notes = dto.Notes;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ReopenDailyClosingAsync(ReopenDailyClosingDto dto)
        {
            var closingDay = await _unitOfWork.DailyClosingRepo.GetByIdAsync(dto.DailyClosingId);
            if (closingDay.IsClosed ==  false) throw new InvalidOperationException();
            closingDay.IsClosed = false;
            closingDay.Notes = dto.Reason;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsDateClosedAsync(DateTime date)
        {
            var checkDayIsClosed = await _unitOfWork.DailyClosingRepo.IsDateClosedAsync(date);
            if (checkDayIsClosed) return true;
            return false;
        }

        public async Task<List<DailyClosingDto>> GetClosingHistoryAsync(DateTime? from, DateTime? to)
        {
            var closingDaysList = await _unitOfWork.DailyClosingRepo.GetHistoryAsync(from, to);
            if (closingDaysList == null || !closingDaysList.Any()) return new List<DailyClosingDto>();
            var mappedClosingDaysList = _mapper.Map<List<DailyClosingDto>>(closingDaysList);
            return mappedClosingDaysList;
        }

    }
}
