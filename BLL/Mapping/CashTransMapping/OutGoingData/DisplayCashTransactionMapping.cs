using AutoMapper;
using BLL.DTOs.Cash.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CashTransMapping.OutGoingData
{
    public class DisplayCashTransactionMapping : Profile
    {
        public DisplayCashTransactionMapping()
        {
            CreateMap<CashTransaction, CashTransactionDto>()
                .ForMember(dest => dest.CustomerName,opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null ))
                .ForMember(dest => dest.VendorName, opt => opt.MapFrom(src => src.Vendor != null ? src.Vendor.Name : null))
                .ForMember(dest => dest.ExpenseCategoryName, opt => opt.MapFrom(src => src.ExpenseCategory != null ? src.ExpenseCategory.Name : null))
                .ForMember(dest => dest.Allocations, opt => opt.MapFrom(src => src.PaymentAllocations != null ? src.PaymentAllocations : null))
                .ForMember(dest => dest.SourceInvoiceNumber, opt => opt.MapFrom(src => src.SourceInvoice.InvoiceNumber != null ? src.SourceInvoice.InvoiceNumber: null));
        }
    }
}
