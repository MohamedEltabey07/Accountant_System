using AutoMapper;
using BLL.DTOs.Dashboard.OutGoingData;
using BLL.IService;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class DashboardService : IDashboardService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IInvoiceService _invoiceService;

        public DashboardService(IInvoiceService invoiceService, IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _invoiceService = invoiceService;
        }

        public async Task<DashboardDto> GetDashboardMetricsAsync(decimal openingCashAmount = 0m)
        {
            var dto = new DashboardDto();

            // Auto-detect opening cash float if not explicitly passed
            if (openingCashAmount == 0m)
            {
                var todayClosing = await _unitOfWork.DailyClosingRepo.GetByDateAsync(DateTime.Today);
                if (todayClosing != null)
                {
                    openingCashAmount = todayClosing.OpeningCashAmount;
                }
                else
                {
                    var latestClosing = await _unitOfWork.DailyClosingRepo.GetLatestClosingAsync();
                    if (latestClosing != null)
                    {
                        openingCashAmount = latestClosing.CountedClosingCashAmount ?? latestClosing.ExpectedClosingCashAmount;
                    }
                }
            }

            // 1. TotalCustomerDebt: Sum of all positive net customer balances (ما لنا عند العملاء)
            var customers = await _unitOfWork.CustomerRepo.GetAllAsync();
            decimal totalCustomerDebt = 0m;
            if (customers != null)
            {
                foreach (var customer in customers)
                {
                    var balance = await _unitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
                    if (balance > 0)
                    {
                        totalCustomerDebt += balance;
                    }
                }
            }
            dto.TotalCustomerDebt = totalCustomerDebt;

            // 2. TotalVendorDebt: Sum of all positive net vendor debts (ما علينا للموردين)
            var vendors = await _unitOfWork.Vendors.GetAllAsync();
            decimal totalVendorDebt = 0m;
            if (vendors != null)
            {
                foreach (var vendor in vendors)
                {
                    var balance = await _unitOfWork.Vendors.GetVendorBalanceAsync(vendor.Id);
                    if (balance > 0)
                    {
                        totalVendorDebt += balance;
                    }
                }
            }
            dto.TotalVendorDebt = totalVendorDebt;

            // TODO: Implement the rest of the metrics
            // dto.TodayCashIn = ...
            // dto.TodayCashOut = ...
            // dto.CurrentDrawerCash = ...
            // dto.OpenInvoicesCount = ...
            // dto.TodayInvoicesCount = ...
            // dto.IsTodayClosed = ...
            var currentDate = DateTime.Today.Date;
            var totalCashIn = await _unitOfWork.CashTransactionRepo.GetTotalCashInAsync(DateTime.Today.Date, DateTime.Today.AddDays(1).Date);
            dto.TodayCashIn = totalCashIn;
            var totalCashOut = await _unitOfWork.CashTransactionRepo.GetTotalCashOutAsync(DateTime.Today.Date, DateTime.Today.AddDays(1).Date);
            dto.TodayCashOut = totalCashOut;
            var allPostedInvoices = await _unitOfWork.InvoiceRepo.GetAllPostedInvoices();
            var countOpenInovices = 0;
            var countTodayInvoices = 0;
            if (allPostedInvoices != null && allPostedInvoices.Any())
            {   
                foreach(var post in allPostedInvoices)
                    {
                        var invoicesStatus = await _invoiceService.GetInvoicePaymentStatusAsync(post.Id);
                        if (invoicesStatus == DAL.Enums.InvoicePaymentStatus.PartiallyPaid || invoicesStatus == DAL.Enums.InvoicePaymentStatus.Unpaid)
                            countOpenInovices++;
                        if ((post.CreatedAt >= DateTime.Today && post.CreatedAt <= DateTime.Today.AddDays(1).Date))
                            countTodayInvoices++;
                    }
            }
            dto.OpenInvoicesCount = countOpenInovices;
            dto.TodayInvoicesCount = countTodayInvoices;
            var checkClosedDay = await _unitOfWork.DailyClosingRepo.IsDateClosedAsync(DateTime.Today);
            dto.IsTodayClosed = checkClosedDay;
            dto.CurrentDrawerCash = (totalCashIn + openingCashAmount) - totalCashOut;
            return dto;
        }
    }
}
