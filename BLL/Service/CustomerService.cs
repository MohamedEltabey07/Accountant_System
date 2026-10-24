using AutoMapper;
using BLL.DTOs.Customer.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Service
{
    public class CustomerService : ICustomerService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public CustomerService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }
        public async Task<bool> CreateCustomerAsync(CreateCustomerDto dto)
        {
            if (dto == null) return false;
            var mapCustomer = _mapper.Map<Customer>(dto);
            await _unitOfWork.CustomerRepo.AddAsync(mapCustomer);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteCustomerAsync(int id)
        {
            var existCustomer = await _unitOfWork.CustomerRepo.GetByIdAsync(id);
            if (existCustomer == null) return false;
            existCustomer.IsActive = false;
            await _unitOfWork.CustomerRepo.Update(existCustomer);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<CustomerDto>> GetActiveCustomersAsync()
        {
            var listOfActiveCustomers = await _unitOfWork.CustomerRepo.GetActiveCustomersAsync();
            if (listOfActiveCustomers == null) return null;
            var mapToCustomerDtoList = _mapper.Map<List<CustomerDto>>(listOfActiveCustomers);
            foreach (var cst in mapToCustomerDtoList)
                cst.CurrentBalance = await GetCustomerBalanceAsync(cst.Id);
            return mapToCustomerDtoList;
        }

        public async Task<List<CustomerDto>> GetAllCustomersAsync()
        {
            var listOfActiveCustomers = await _unitOfWork.CustomerRepo.GetAllAsync();
            if (listOfActiveCustomers == null) return null;
            var mapToCustomerDtoList = _mapper.Map<List<CustomerDto>>(listOfActiveCustomers);
            foreach (var cst in mapToCustomerDtoList)
                cst.CurrentBalance = await GetCustomerBalanceAsync(cst.Id);
            return mapToCustomerDtoList;
        }

        public async Task<decimal> GetCustomerBalanceAsync(int customerId)
        {
            var customerBalance = await _unitOfWork.CustomerRepo.GetCustomerBalanceAsync(customerId);
            return customerBalance;
        }

        public async Task<CustomerDto?> GetCustomerByIdAsync(int id)
        {
            var customer = await _unitOfWork.CustomerRepo.GetByIdAsync(id);
            if (customer == null) return null;
            var mapToCustomerDto = _mapper.Map<CustomerDto>(customer);
            mapToCustomerDto.CurrentBalance = await GetCustomerBalanceAsync(customer.Id);
            return mapToCustomerDto;
        }

        public async Task<bool> ToggleStatusAsync(int id)
        {
            var customer = await _unitOfWork.CustomerRepo.GetByIdAsync(id);
            if (customer == null) return false;
            customer.IsActive = !customer.IsActive;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateCustomerAsync(UpdateCustomerDto dto)
        {
            if (dto == null) return false;
            var existanceCustomer = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.Id);
            if (existanceCustomer == null) return false;
            _mapper.Map(dto, existanceCustomer);
            existanceCustomer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
