using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Maritime supply order statement data: the external supply order number is
    // the primary identifier of every order, and both statement modes are fed
    // by the same GetMaritimeSupplyOrdersAsync query so their totals agree.
    public class MaritimeStatementTests : IDisposable
    {
        private readonly TestHarness _harness;

        public MaritimeStatementTests()
        {
            _harness = new TestHarness();
        }

        private async Task<int> PostSupplyOrderAsync(int companyId, string orderNo, List<(int ProductId, decimal Qty, decimal? Price, bool FromFreezer)> items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = companyId,
                ExternalOrderNumber = orderNo,
                SupplyOrderDate = DateTime.Today
            });

            foreach (var (productId, qty, price, fromFreezer) in items)
            {
                await _harness.MarkedItemAddAsync(draftId, productId, qty, price, fromFreezer);
            }

            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.True(posted);
            return draftId;
        }

        [Fact]
        public async Task SupplyOrders_ReturnExternalNumber_Date_Total_AndItemDetails()
        {
            var company = _harness.SeedMaritimeCompany("شركة كشف 1");
            var fish = _harness.SeedFrozenFish("سمك جودا", 10m, 100m);
            var rice = _harness.SeedRegularProduct("أرز قصير");
            await _harness.CreateStockInAsync(fish.Id, 30m); // 300 kg

            await PostSupplyOrderAsync(company.Id, "ORDER-001", new List<(int, decimal, decimal?, bool)>
            {
                (fish.Id, 200m, null, true),
            });
            await PostSupplyOrderAsync(company.Id, "ORDER-002", new List<(int, decimal, decimal?, bool)>
            {
                (rice.Id, 50m, 80m, false),
            });

            var orders = await _harness.InvoiceService.GetMaritimeSupplyOrdersAsync(company.Id, null, null);

            Assert.Equal(2, orders.Count);

            var order1 = orders.Single(o => o.ExternalOrderNumber == "ORDER-001");
            Assert.StartsWith("MAR-", order1.InvoiceNumber); // internal document number kept separately from the external one
            Assert.Equal(DateTime.Today, order1.TransactionDate.Date);
            Assert.Equal("شركة كشف 1", order1.CompanyName);
            Assert.Equal(200m * 100m, order1.TotalAmount);

            var item1 = order1.Items.Single();
            Assert.Equal("سمك جودا", item1.ProductName);
            Assert.Equal(200m, item1.Quantity); // required weight in KG
            Assert.Equal(100m, item1.UnitPrice);
            Assert.True(item1.IsFromFreezer);

            var order2 = orders.Single(o => o.ExternalOrderNumber == "ORDER-002");
            Assert.Equal(50m * 80m, order2.TotalAmount);
            Assert.False(order2.Items.Single().IsFromFreezer);

            // Display identifier: the EXTERNAL supply order number.
            Assert.Equal("ORDER-001", order1.DisplayOrderNumber);
            Assert.Equal("ORDER-002", order2.DisplayOrderNumber);
        }

        [Fact]
        public async Task SummaryAndDetailed_FinancialTotals_Agree()
        {
            var company = _harness.SeedMaritimeCompany("شركة كشف 2");
            var fish = _harness.SeedFrozenFish("سمك التوحيد", 10m, 60m);
            var rice = _harness.SeedRegularProduct("مكرونات");
            await _harness.CreateStockInAsync(fish.Id, 20m);

            await PostSupplyOrderAsync(company.Id, "ST-1", new List<(int, decimal, decimal?, bool)>
            {
                (fish.Id, 100m, null, true),
                (rice.Id, 25m, 25m, false),
            });
            await PostSupplyOrderAsync(company.Id, "ST-2", new List<(int, decimal, decimal?, bool)>
            {
                (fish.Id, 30m, null, true),
            });

            var orders = await _harness.InvoiceService.GetMaritimeSupplyOrdersAsync(company.Id, null, null);

            // Summary statement rows are built from the posted maritime invoices;
            // the detailed blocks come from GetMaritimeSupplyOrdersAsync — both
            // must produce the same order total and date for the same filters.
            var invoices = await _harness.InvoiceService.GetInvoicesAsync(InvoiceType.MaritimeSupply, InvoiceStatus.Posted);
            var companyInvoices = invoices!.Where(i => i.CustomerId == company.Id).ToList();

            Assert.Equal(
                companyInvoices.Sum(i => i.TotalAmount),
                orders.Sum(o => o.TotalAmount));

            foreach (var invoice in companyInvoices)
            {
                var order = orders.Single(o => o.InvoiceId == invoice.Id);
                Assert.Equal(invoice.TotalAmount, order.TotalAmount);
                Assert.Equal(invoice.TransactionDate.Date, order.TransactionDate.Date);
                Assert.Equal(invoice.ExternalOrderNumber, order.ExternalOrderNumber);
            }

            // Per-order item sums also reconcile with the item-level math.
            foreach (var order in orders)
            {
                Assert.Equal(order.TotalAmount, order.Items.Sum(i => i.LineTotal) - order.Items.Sum(i => i.LineDiscountAmount));
            }
        }

        [Fact]
        public async Task MixedOrder_StatementShows_SourcePerItem()
        {
            var company = _harness.SeedMaritimeCompany("شركة كشف 3");
            var fish = _harness.SeedFrozenFish("سمك علامات", 10m, 50m);
            var rice = _harness.SeedRegularProduct("شاي معبأ");
            await _harness.CreateStockInAsync(fish.Id, 10m);

            await PostSupplyOrderAsync(company.Id, "MIX-1", new List<(int, decimal, decimal?, bool)>
            {
                (fish.Id, 100m, null, true),
                (rice.Id, 10m, 40m, false),
            });

            var orders = await _harness.InvoiceService.GetMaritimeSupplyOrdersAsync(company.Id, null, null);
            var order = orders.Single();

            Assert.True(order.Items.Single(i => i.ProductName == "سمك علامات").IsFromFreezer);
            Assert.False(order.Items.Single(i => i.ProductName == "شاي معبأ").IsFromFreezer);

            // Detailed statement total still counts both items.
            Assert.Equal(100m * 50m + 10m * 40m, order.TotalAmount);
        }

        public void Dispose() => _harness.Dispose();
    }

    // Small extension to keep the seeding helper list-friendly.
    internal static class MaritimeStatementTestExtensions
    {
        public static async Task MarkedItemAddAsync(this TestHarness harness, int draftId, int productId, decimal qty, decimal? price, bool fromFreezer)
        {
            await harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = productId,
                Quantity = qty,
                UnitPrice = price,
                IsFromFreezer = fromFreezer
            });
        }
    }
}
