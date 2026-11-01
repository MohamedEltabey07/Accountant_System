using AutoMapper;
using BLL.DTOs.Payment.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.Payment.OutGoingData
{
    public class OpenInvoicesForPaymentMapping : Profile
    {
        public OpenInvoicesForPaymentMapping()
        {
            CreateMap<Invoice, OpenInvoiceForPaymentDto>();
        }
    }
}
