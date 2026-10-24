using BLL.DTOs.Customer.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.IService
{
    public interface ICustomerService
    {
        public Task<List<CustomerDto>> GetAllCustomersAsync();
        public Task<List<CustomerDto>> GetActiveCustomersAsync();
        public Task<CustomerDto?> GetCustomerByIdAsync(int id);
        public Task<decimal> GetCustomerBalanceAsync(int customerId);
        public Task<bool> CreateCustomerAsync(CreateCustomerDto dto);
        public Task<bool> UpdateCustomerAsync(UpdateCustomerDto dto);
        public Task<bool> ToggleStatusAsync(int id);
        public Task<bool> DeleteCustomerAsync(int id);
    }
}
