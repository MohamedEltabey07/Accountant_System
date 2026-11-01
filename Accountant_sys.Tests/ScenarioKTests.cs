using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Scenario K (acceptance): independent per-invoice closure. A payment allocated
    // to one invoice must never spill over into the customer's other invoices —
    // every invoice keeps its own settlement state.
    public class ScenarioKTests : IDisposable
    {
        private readonly TestHarness _harness;

        public ScenarioKTests()
        {
            _harness = new TestHarness();
        }

        private async Task<int> CreateWholesaleInvoiceAsync(int customerId, string productName, decimal quantity, decimal unitPrice)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customerId
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = _productId[productName],
                Quantity = quantity,
                UnitPrice = unitPrice
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            return draftId;
        }

        private readonly Dictionary<string, int> _productId = new();

        [Fact]
        public async Task PaymentAllocatedToInvoice1_DoesNotSettleInvoices2And3()
        {
            // Arrange: one wholesale customer with 3 posted invoices (5,000 / 3,000 / 2,000).
            var fish = _harness.SeedFrozenFish("سمك بلطي جملة", 10m);
            _productId["سمك بلطي جملة"] = fish.Id;
            var customer = _harness.SeedWholesaleCustomer("عميل السيناريو K");

            var invoice1 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك بلطي جملة", 100m, 50m); // 5,000
            var invoice2 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك بلطي جملة", 60m, 50m);  // 3,000
            var invoice3 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك بلطي جملة", 40m, 50m);  // 2,000

            Assert.Equal(5000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice1));
            Assert.Equal(3000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice2));
            Assert.Equal(2000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice3));

            var preStatus1 = await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice1);
            Assert.Equal(InvoicePaymentStatus.Unpaid, preStatus1);

            // Act 1: pay exactly 5,000 — allocated strictly to Invoice #1.
            var cashTxId = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 5000m,
                Notes = "سداد الفاتورة الأولى فقط",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = invoice1, AllocatedAmount = 5000m }
                }
            });
            Assert.True(cashTxId > 0);

            // Assert 1: Invoice #1 fully paid; #2 and #3 untouched.
            Assert.Equal(0m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice1));
            Assert.Equal(InvoicePaymentStatus.Paid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice1));

            Assert.Equal(3000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice2));
            Assert.Equal(InvoicePaymentStatus.Unpaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice2));

            Assert.Equal(2000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice3));
            Assert.Equal(InvoicePaymentStatus.Unpaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice3));

            // No spillover: allocation rows reference Invoice #1 only.
            var allocationsForTx = _harness.Context.Set<PaymentAllocation>()
                .Where(pa => pa.CashTransactionId == cashTxId).ToList();
            Assert.Single(allocationsForTx);
            Assert.Equal(invoice1, allocationsForTx[0].InvoiceId);
            Assert.Equal(5000m, allocationsForTx[0].Amount);
            Assert.Empty(_harness.Context.Set<PaymentAllocation>().Where(pa => pa.InvoiceId == invoice2));
            Assert.Empty(_harness.Context.Set<PaymentAllocation>().Where(pa => pa.InvoiceId == invoice3));

            // Customer-level ledger is also exact: 10,000 debt - 5,000 paid = 5,000.
            Assert.Equal(5000m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id));

            // Act 2: allocate 1,500 to Invoice #2 → PartiallyPaid, 1,500 remaining.
            var secondTxId = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 1500m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = invoice2, AllocatedAmount = 1500m }
                }
            });
            Assert.True(secondTxId > 0);

            Assert.Equal(1500m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice2));
            Assert.Equal(InvoicePaymentStatus.PartiallyPaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice2));

            // Invoice #3 still strictly Unpaid after the second payment too.
            Assert.Equal(InvoicePaymentStatus.Unpaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice3));
        }

        [Fact]
        public async Task MultiInvoicePartialSplit_KeepsEachClosuredExactly()
        {
            var fish = _harness.SeedFrozenFish("سمك قاروص جملة", 10m);
            _productId["سمك قاروص جملة"] = fish.Id;
            var customer = _harness.SeedWholesaleCustomer("عميل توزيع متعدد");

            var invoice1 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك قاروص جملة", 100m, 50m); // 5,000
            var invoice2 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك قاروص جملة", 60m, 50m);  // 3,000
            var invoice3 = await CreateWholesaleInvoiceAsync(customer.Id, "سمك قاروص جملة", 40m, 50m);  // 2,000

            // One payment of 2,000 split across #1 (1,500) and #2 (500): each keeps its own state.
            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 2000m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = invoice1, AllocatedAmount = 1500m },
                    new() { InvoiceId = invoice2, AllocatedAmount = 500m }
                }
            });

            Assert.Equal(3500m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice1));
            Assert.Equal(2500m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice2));
            Assert.Equal(2000m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(invoice3));

            Assert.Equal(InvoicePaymentStatus.PartiallyPaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice1));
            Assert.Equal(InvoicePaymentStatus.PartiallyPaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice2));
            Assert.Equal(InvoicePaymentStatus.Unpaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(invoice3));
        }

        public void Dispose() => _harness.Dispose();
    }
}
