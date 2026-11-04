using AutoMapper;
using BLL.DTOs.Invoice.OutGoingData;
using DAL.Entities;

namespace BLL.Mapping.InvoiceMapping.OutGoingData
{
    public class InvoiceChargeProfile : Profile
    {
        public InvoiceChargeProfile()
        {
            CreateMap<InvoiceAdditionalCharge, InvoiceChargeDto>();
            CreateMap<InvoiceAmendment, InvoiceAmendmentDto>();
        }
    }
}
