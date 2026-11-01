using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Maritime companies live on the shared customer accounting stack, so the
    // critical guarantee is isolation: they stay in their own screens, cannot be
    // edited through the wholesale customer service, and keep single stocks.
    public class MaritimeCompanyTests : IDisposable
    {
        private readonly TestHarness _harness;

        public MaritimeCompanyTests()
        {
            _harness = new TestHarness();
        }

        [Fact]
        public async Task CreateCompany_IsStoredAsCustomerTypeMaritime_AndExcludedFromWholesaleLists()
        {
            var wholesale = _harness.SeedWholesaleCustomer("عميل جملة حقيقي");
            await _harness.MaritimeService.CreateCompanyAsync(new CreateMaritimeCompanyDto
            {
                Name = "شركة سمك البحر",
                PhoneNumber = "0100000000",
                Address = "بورسعيد"
            });

            var wholesaleList = await _harness.CustomerService.GetActiveCustomersAsync();
            Assert.Single(wholesaleList!);
            Assert.Equal(wholesale.Id, wholesaleList!.Single().Id);

            var maritimeFreeDirectory = await _harness.CustomerService.GetAllCustomersAsync();
            Assert.Single(maritimeFreeDirectory!); // only the wholesale customer; maritime companies never appear

            var byType = await _harness.UnitOfWork.CustomerRepo.GetActiveByTypeAsync(CustomerType.MaritimeCompany);
            Assert.Single(byType);
        }

        [Fact]
        public async Task MaritimeService_CannotTouchWholesaleCustomer()
        {
            var wholesale = _harness.SeedWholesaleCustomer("عميل جملة محمي");

            // Only maritime companies are legible/editable via the maritime screen.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.MaritimeService.UpdateCompanyAsync(new UpdateMaritimeCompanyDto
                {
                    Id = wholesale.Id,
                    Name = "اسم جديد خطير"
                }));
        }

        [Fact]
        public async Task MaritimeBalances_StayIndependentFromWholesaleAccounts()
        {
            // Both parties buy the same fish; each order is fully independent.
            var fish = _harness.SeedFrozenFish("سمك كبير", 10m, 40m);
            var companyA = _harness.SeedMaritimeCompany("شركة ملاحة A");
            var companyB = _harness.SeedMaritimeCompany("شركة ملاحة B");
            await _harness.CreateStockInAsync(fish.Id, 30m); // 300 kg shared in the same freezers

            var invoiceIds = new List<(int CompanyId, int InvoiceId)>();
            foreach (var (company, qty, orderNo) in new[] { (companyA, 40m, "SO-A1"), (companyB, 80m, "SO-B1") })
            {
                var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
                {
                    Type = InvoiceType.MaritimeSupply,
                    TransactionDate = DateTime.Today,
                    CustomerId = company.Id,
                    ExternalOrderNumber = orderNo,
                    SupplyOrderDate = DateTime.Today
                });
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = fish.Id,
                    Quantity = qty
                });
                await _harness.InvoiceService.PostInvoiceAsync(draftId);
                invoiceIds.Add((company.Id, draftId));
            }

            // 300 - (40 + 80) = 180 kg shared across the two freezers (one logical location).
            Assert.Equal(180m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Each company's ledger is its own: A owes 1600, B owes 3200.
            Assert.Equal(1600m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(companyA.Id));
            Assert.Equal(3200m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(companyB.Id));

            // A's payment only settles A's debt.
            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = companyA.Id,
                PaymentDate = DateTime.Today,
                Amount = 1600m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = invoiceIds.Single(x => x.CompanyId == companyA.Id).InvoiceId, AllocatedAmount = 1600m }
                }
            });

            Assert.Equal(0m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(companyA.Id));
            Assert.Equal(3200m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(companyB.Id));
        }

        [Fact]
        public async Task MaritimeStatement_Orders_TotalFromWeightXPrice()
        {
            var fish = _harness.SeedFrozenFish("سمك تونة", 10m, 60m);
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 20");
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-T20",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 25.5m
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(25.5m, invoice!.Items.Single().Quantity); // kg preserved (fractional)
            Assert.Equal(25.5m * 60m, invoice.TotalAmount);
            Assert.Equal("شركة ملاحة 20", invoice.CustomerName);
        }

        public void Dispose() => _harness.Dispose();
    }
}
