using DAL.Entities;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.IRepos
{
    public interface ICustomerRepo : IGenericRepo<Customer>
    {
        public Task<List<Customer>> GetActiveCustomersAsync();
        public Task<List<Customer>> GetActiveByTypeAsync(CustomerType type);
        public Task<List<Customer>> GetByTypeAsync(CustomerType type);
        public Task<Customer?> GetCustomerWithFinancialDetailsAsync(int customerId);
        public Task<bool> HasFinancialHistoryAsync(int customerId);
        public Task<decimal> GetCustomerBalanceAsync(int customerId);
    }
}
