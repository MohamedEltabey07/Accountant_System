using AutoMapper;
using BLL.DTOs.Payment.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.Payment.OutGoingData
{
    public class PaymentAllocationProfile : Profile
    {
        public PaymentAllocationProfile()
        {
            CreateMap<PaymentAllocation, PaymentAllocationDto>()
                .ForMember(dest => dest.InvoiceNumber , opt => opt.MapFrom(src => src.Invoice.InvoiceNumber));
        }
    }
}
