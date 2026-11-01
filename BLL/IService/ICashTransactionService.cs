using BLL.DTOs.Cash.InComingData;
using BLL.DTOs.Cash.OutGoingData;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface ICashTransactionService
    {
        Task<bool> RecordCashMovementAsync(CreateCashMovementDto dto);
        Task<int> RecordExpenseAsync(CreateCashMovementDto dto);
        Task<List<CashTransactionDto>> GetTransactionsAsync(DateTime? from, DateTime? to, CashCategory? category, CashDirection? direction);
        Task<CashTransactionDto?> GetTransactionByIdAsync(int id);
        Task<DailyCashSummaryDto> GetDailyCashSummaryAsync(DateTime date);
    }
}
