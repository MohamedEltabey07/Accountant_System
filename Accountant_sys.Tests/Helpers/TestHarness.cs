using AutoMapper;
using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Invoice.InComingData;
using BLL.IService;
using BLL.Service;
using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accountant_sys.Tests.Helpers
{
    // Shared in-memory test harness: one EF Core InMemory database per test class
    // instance, real services on top of a real UnitOfWork (no mocks of business
    // logic). InMemory transactions are ignored with a warning suppression since
    // the InMemory provider does not support them (SQL Server does in production).
    public sealed class TestHarness : IDisposable
    {
        public AppDbcontext Context { get; }
        public IUnitOfWork UnitOfWork { get; }
        public IMapper Mapper { get; }

        public ProductService ProductService { get; }
        public StockMovementService StockService { get; }
        public InvoiceService InvoiceService { get; }
        public PaymentService PaymentService { get; }
        public CreditNoteService CreditNoteService { get; }
        public MaritimeCompanyService MaritimeService { get; }
        public CustomerService CustomerService { get; }

        public TestHarness()
        {
            var options = new DbContextOptionsBuilder<AppDbcontext>()
                .UseInMemoryDatabase($"AccountantTest_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            Context = new AppDbcontext(options);
            UnitOfWork = new UnitOfWork(Context);
            Mapper = new MapperConfiguration(cfg =>
                cfg.AddMaps(typeof(InvoiceService).Assembly), NullLoggerFactory.Instance).CreateMapper();

            ProductService = new ProductService(UnitOfWork, Mapper);
            StockService = new StockMovementService(UnitOfWork, ProductService);
            InvoiceService = new InvoiceService(UnitOfWork, Mapper, ProductService);
            PaymentService = new PaymentService(UnitOfWork, Mapper, InvoiceService);
            CreditNoteService = new CreditNoteService(UnitOfWork, Mapper);
            CustomerService = new CustomerService(UnitOfWork, Mapper);
            MaritimeService = new MaritimeCompanyService(UnitOfWork, Mapper, CustomerService);
        }

        public Product SeedFrozenFish(string name, decimal defaultCartonWeightKg, decimal? maritimePricePerKg = null, decimal purchase = 20m)
        {
            var product = new Product
            {
                Name = name,
                PurchasePrice = purchase,
                WholesalePrice = purchase + 5m,
                RetailPrice = purchase + 15m,
                Category = ProductCategory.FrozenFish,
                DefaultCartonWeightKg = defaultCartonWeightKg,
                MaritimePricePerKg = maritimePricePerKg,
                IsActive = true
            };
            UnitOfWork.ProductRepo.AddAsync(product).Wait();
            UnitOfWork.SaveChangesAsync().Wait();
            return product;
        }

        public Product SeedRegularProduct(string name)
        {
            var product = new Product
            {
                Name = name,
                PurchasePrice = 10m,
                WholesalePrice = 12m,
                RetailPrice = 15m,
                Category = ProductCategory.RegularProduct,
                IsActive = true
            };
            UnitOfWork.ProductRepo.AddAsync(product).Wait();
            UnitOfWork.SaveChangesAsync().Wait();
            return product;
        }

        public Customer SeedWholesaleCustomer(string name)
        {
            var customer = new Customer { Name = name, IsActive = true, Type = CustomerType.WholesaleCustomer };
            UnitOfWork.CustomerRepo.AddAsync(customer).Wait();
            UnitOfWork.SaveChangesAsync().Wait();
            return customer;
        }

        public CompanyInfo SeedMaritimeCompany(string name)
        {
            var customer = new Customer { Name = name, IsActive = true, Type = CustomerType.MaritimeCompany };
            UnitOfWork.CustomerRepo.AddAsync(customer).Wait();
            UnitOfWork.SaveChangesAsync().Wait();
            return new CompanyInfo(customer.Id, customer.Name);
        }

        public async Task<int> CreateStockInAsync(int productId, decimal cartons, decimal? weightPerCarton = null, string notes = "استلام")
        {
            await StockService.RecordStockInAsync(new CreateStockInDto
            {
                ProductId = productId,
                Cartons = cartons,
                WeightPerCartonKg = weightPerCarton,
                Notes = notes
            });
            return productId;
        }

        public async Task<int> CreateMaritimeDraftAsync(int companyId, int fishId, string orderNo, decimal qty)
        {
            var draftId = await InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = companyId,
                ExternalOrderNumber = orderNo,
                SupplyOrderDate = DateTime.Today
            });

            await InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fishId,
                Quantity = qty
            });

            return draftId;
        }

        public record CompanyInfo(int Id, string Name);

        public void Dispose()
        {
            Context.Dispose();
        }
    }
}
