using AutoMapper;
using BLL.DTOs.Invoice.InComingData;
using DAL.Entities;

namespace BLL.Mapping.InvoiceMapping.InComingData
{
    public class CreateInvoiceProfile : Profile
    {
        public CreateInvoiceProfile()
        {
            CreateMap<CreateInvoiceDraftDto, Invoice>();
        }
    }
}
