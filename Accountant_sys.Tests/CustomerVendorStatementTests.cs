using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using BLL.Service;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    public class CustomerVendorStatementTests : IDisposable
    {
        private readonly TestHarness _harness;

        public CustomerVendorStatementTests()
        {
            _harness = new TestHarness();
        }

        private Vendor SeedVendor(string name)
        {
            var vendor = new Vendor { Name = name, IsActive = true };
            _harness.UnitOfWork.Vendors.AddAsync(vendor).Wait();
            _harness.UnitOfWork.SaveChangesAsync().Wait();
            return vendor;
        }

        private async Task<int> PostCustomerInvoiceAsync(
            int customerId,
            DateTime date,
            decimal invoiceDiscount,
            string? notes,
            List<(int ProductId, decimal Qty, decimal Price, decimal LineDiscount)> items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = date,
                CustomerId = customerId,
                InvoiceDiscountAmount = invoiceDiscount,
                Notes = notes
            });

            foreach (var (productId, qty, price, lineDiscount) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price,
                    LineDiscountAmount = lineDiscount
                });
            }

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return draftId;
        }

        private async Task<int> PostVendorInvoiceAsync(
            int vendorId,
            DateTime date,
            List<(int ProductId, decimal Qty, decimal Price, decimal LineDiscount)> items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.VendorPurchase,
                TransactionDate = date,
                VendorId = vendorId
            });

            foreach (var (productId, qty, price, lineDiscount) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price,
                    LineDiscountAmount = lineDiscount
                });
            }

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return draftId;
        }

        [Fact]
        public async Task CustomerDetailedStatement_ReturnsPostedWholesaleInvoices_WithItemsAndDiscounts()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل تجريبي للجملة");
            var fish = _harness.SeedFrozenFish("سمك ماكريل", 10m, 40m);
            var rice = _harness.SeedRegularProduct("أرز بسمتي");

            await PostCustomerInvoiceAsync(
                customer.Id,
                DateTime.Today.AddDays(-2),
                invoiceDiscount: 25m,
                notes: "توريد صباحي",
                items: new List<(int, decimal, decimal, decimal)>
                {
                    (fish.Id, 10m, 100m, 50m),   // 1000 - 50 = 950
                    (rice.Id, 5m, 60m, 0m)       // 300
                });                              // SubTotal 1300, ItemDisc 50, InvDisc 25 => Total 1225

            await PostCustomerInvoiceAsync(
                customer.Id,
                DateTime.Today,
                invoiceDiscount: 0m,
                notes: null,
                items: new List<(int, decimal, decimal, decimal)>
                {
                    (rice.Id, 20m, 55m, 0m)      // 1100
                });

            var details = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.WholesaleSales, customer.Id, null, null, null);

            Assert.Equal(2, details.Count);

            var first = details[0];
            Assert.StartsWith("SAL-", first.InvoiceNumber);
            Assert.Equal("عميل تجريبي للجملة", first.PartyName);
            Assert.Equal("توريد صباحي", first.Notes);
            Assert.Equal(1300m, first.SubTotal);
            Assert.Equal(50m, first.ItemDiscountTotal);
            Assert.Equal(25m, first.InvoiceDiscountAmount);
            Assert.Equal(1225m, first.TotalAmount);
            Assert.Equal(2, first.Items.Count);
            Assert.Equal("سمك ماكريل", first.Items[0].ProductName);
            Assert.Equal(10m, first.Items[0].Quantity);
            Assert.Equal(100m, first.Items[0].UnitPrice);
            Assert.Equal(50m, first.Items[0].LineDiscountAmount);
            Assert.Equal(950m, first.Items[0].LineTotal);

            var second = details[1];
            Assert.Equal(1100m, second.TotalAmount);
            Assert.Single(second.Items);
        }

        [Fact]
        public async Task VendorDetailedStatement_IsolatesInvoicesPerVendor_AndExcludesCustomerSales()
        {
            var vendor1 = SeedVendor("مورد الأسماك الأول");
            var vendor2 = SeedVendor("مورد آخر");
            var customer = _harness.SeedWholesaleCustomer("عميل مستقل");
            var product = _harness.SeedRegularProduct("كرتونة تعبئة");

            await PostVendorInvoiceAsync(vendor1.Id, DateTime.Today.AddDays(-1),
                new List<(int, decimal, decimal, decimal)> { (product.Id, 12m, 50m, 0m) });
            await PostVendorInvoiceAsync(vendor1.Id, DateTime.Today,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 8m, 45m, 10m) });
            await PostVendorInvoiceAsync(vendor2.Id, DateTime.Today,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 100m, 99m, 0m) });
            await PostCustomerInvoiceAsync(customer.Id, DateTime.Today, 0m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 5m, 80m, 0m) });

            var details = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.VendorPurchase, null, vendor1.Id, null, null);

            Assert.Equal(2, details.Count);
            Assert.All(details, d =>
            {
                Assert.StartsWith("PUR-", d.InvoiceNumber);
                Assert.Equal(vendor1.Id, d.VendorId);
                Assert.Equal("مورد الأسماك الأول", d.PartyName);
            });
            Assert.Equal(600m + 350m, details.Sum(d => d.TotalAmount));
        }

        [Fact]
        public async Task StatementDetails_ExcludesDraftAndCancelledInvoices()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل حالات الفواتير");
            var product = _harness.SeedRegularProduct("صنف قياسي");

            // Draft invoice (not posted)
            var draftOnlyId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftOnlyId,
                ProductId = product.Id,
                Quantity = 5m,
                UnitPrice = 20m
            });

            // Posted then cancelled invoice
            var cancelledId = await PostCustomerInvoiceAsync(customer.Id, DateTime.Today, 0m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 7m, 30m, 0m) });
            Assert.True(await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = cancelledId,
                CancellationReason = "إلغاء للاختبار"
            }));

            // Active posted invoice
            var activeId = await PostCustomerInvoiceAsync(customer.Id, DateTime.Today, 0m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 4m, 25m, 0m) });

            var details = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.WholesaleSales, customer.Id, null, null, null);

            var only = Assert.Single(details);
            Assert.Equal(activeId, only.InvoiceId);
            Assert.Equal(100m, only.TotalAmount);
        }

        [Fact]
        public async Task StatementDetails_DateFilter_MatchesInclusiveStatementBoundaries()
        {
            var vendor = SeedVendor("مورد فلترة التاريخ");
            var product = _harness.SeedRegularProduct("ثلج مجروش");

            await PostVendorInvoiceAsync(vendor.Id, DateTime.Today.AddDays(-10),
                new List<(int, decimal, decimal, decimal)> { (product.Id, 1m, 100m, 0m) });
            var inWindowId = await PostVendorInvoiceAsync(vendor.Id, DateTime.Today.AddDays(-5).AddHours(14),
                new List<(int, decimal, decimal, decimal)> { (product.Id, 2m, 150m, 0m) });
            await PostVendorInvoiceAsync(vendor.Id, DateTime.Today,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 3m, 200m, 0m) });

            var targetDay = DateTime.Today.AddDays(-5);
            var details = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.VendorPurchase, null, vendor.Id, targetDay, targetDay);

            var only = Assert.Single(details);
            Assert.Equal(inWindowId, only.InvoiceId);
            Assert.Equal(300m, only.TotalAmount);
        }

        [Fact]
        public async Task SummaryAndDetailed_CustomerInvoiceTotals_Agree()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل مطابقة الإجماليات");
            var product = _harness.SeedRegularProduct("سمك طازج");

            await PostCustomerInvoiceAsync(customer.Id, DateTime.Today.AddDays(-1), 15m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 10m, 50m, 5m) });
            await PostCustomerInvoiceAsync(customer.Id, DateTime.Today, 0m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 6m, 40m, 0m) });

            var summaryPosted = (await _harness.InvoiceService.GetInvoicesAsync(InvoiceType.WholesaleSales, InvoiceStatus.Posted))
                .Where(i => i.CustomerId == customer.Id)
                .ToList();
            var detailed = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.WholesaleSales, customer.Id, null, null, null);

            Assert.Equal(summaryPosted.Count, detailed.Count);
            Assert.Equal(summaryPosted.Sum(i => i.TotalAmount), detailed.Sum(d => d.TotalAmount));
        }

        [Fact]
        public async Task VendorPayments_ReturnChronologicalOrder_WithInvoiceAllocationsAndNotes()
        {
            var vendor = SeedVendor("مورد الدفعات المتتابعة");
            var product = _harness.SeedRegularProduct("سمك فيليه");

            var inv1Id = await PostVendorInvoiceAsync(vendor.Id, DateTime.Today,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 10m, 100m, 0m) }); // 1000

            var pay1Id = await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = DateTime.Today,
                Amount = 300m,
                Notes = "دفعة أولى",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new InvoiceAllocationItemDto { InvoiceId = inv1Id, AllocatedAmount = 300m }
                }
            });

            var pay2Id = await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = DateTime.Today,
                Amount = 200m,
                Notes = "دفعة ثانية",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new InvoiceAllocationItemDto { InvoiceId = inv1Id, AllocatedAmount = 200m }
                }
            });

            var cashService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            var txns = await cashService.GetTransactionsAsync(
                DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1),
                CashCategory.VendorPayment, CashDirection.Out);

            var vendorTxns = txns.Where(t => t.VendorId == vendor.Id).ToList();
            Assert.Equal(2, vendorTxns.Count);
            // Same-day payments must come out oldest -> newest (not reversed!)
            Assert.Equal(pay1Id, vendorTxns[0].Id);
            Assert.Equal(pay2Id, vendorTxns[1].Id);
            Assert.Equal("دفعة أولى", vendorTxns[0].Notes);
            Assert.Single(vendorTxns[0].Allocations);
            Assert.StartsWith("PUR-", vendorTxns[0].Allocations[0].InvoiceNumber);
            Assert.Equal(500m, await _harness.VendorService.GetVendorBalanceAsync(vendor.Id));
        }

        [Fact]
        public async Task CustomerPayments_ReturnChronologicalOrder_WithInvoiceAllocationsAndNotes()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل الدفعات المتتابعة");
            var product = _harness.SeedRegularProduct("جمبري");

            var invId = await PostCustomerInvoiceAsync(customer.Id, DateTime.Today, 0m, null,
                new List<(int, decimal, decimal, decimal)> { (product.Id, 10m, 120m, 0m) }); // 1200

            var rec1Id = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 400m,
                Notes = "تحصيل أول",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new InvoiceAllocationItemDto { InvoiceId = invId, AllocatedAmount = 400m }
                }
            });

            var rec2Id = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 500m,
                Notes = "تحصيل ثاني",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new InvoiceAllocationItemDto { InvoiceId = invId, AllocatedAmount = 500m }
                }
            });

            var cashService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            var txns = await cashService.GetTransactionsAsync(
                DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1),
                CashCategory.CustomerPayment, CashDirection.In);

            var customerTxns = txns.Where(t => t.CustomerId == customer.Id).ToList();
            Assert.Equal(2, customerTxns.Count);
            Assert.Equal(rec1Id, customerTxns[0].Id);
            Assert.Equal(rec2Id, customerTxns[1].Id);
            Assert.Equal("تحصيل أول", customerTxns[0].Notes);
            Assert.Single(customerTxns[0].Allocations);
            Assert.StartsWith("SAL-", customerTxns[0].Allocations[0].InvoiceNumber);
            Assert.Equal(300m, await _harness.CustomerService.GetCustomerBalanceAsync(customer.Id));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }
    }
}
