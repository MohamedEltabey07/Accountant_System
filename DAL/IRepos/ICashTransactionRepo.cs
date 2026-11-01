using DAL.Entities;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface ICashTransactionRepo : IGenericRepo<CashTransaction>
    {
        public Task<CashTransaction?> GetByIdWithDetailsAsync(int id);
        public Task<List<CashTransaction>> GetFilteredTransactionsAsync(DateTime? fromDate, DateTime? toDate, CashCategory? category, CashDirection? direction);
        public Task<List<CashTransaction>> GetCustomerPaymentsAsync(int customerId);
        public Task<List<CashTransaction>> GetVendorPaymentsAsync(int vendorId);
        public Task<decimal> GetTotalCashInAsync(DateTime fromDate, DateTime toDate);
        public Task<decimal> GetTotalCashOutAsync(DateTime fromDate, DateTime toDate);
    }
}
