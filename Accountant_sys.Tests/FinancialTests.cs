using Accountant_sys.Tests.Helpers;
using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Financial flow tests: invoicing, per-invoice settlement, payment status
    // derivation, customer balance formula, and type-separated payment flows.
    public class FinancialTests : IDisposable
    {
        private readonly TestHarness _harness;

        public FinancialTests()
        {
            _harness = new TestHarness();
        }

        [Fact]
        public async Task WholesaleInvoicePost_DoesNotTouchStock()
        {
            var fish = _harness.SeedFrozenFish("سمك بلطي جملة", 10m, 30m);
            var customer = _harness.SeedWholesaleCustomer("عميل جملة 1");
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });

            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 100m,
                UnitPrice = 35m
            });

            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            // Wholesale flow is a pure accounting flow — freezers are untouched.
            Assert.Equal(100m, (_harness.Context.StockMovements.Single(m => m.ProductId == fish.Id)).TotalWeightKg);
            Assert.Equal(1, _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id));
        }

        [Fact]
        public async Task Payment_ReducesPerInvoiceBalance_AndDerivesStatus()
        {
            var fish = _harness.SeedFrozenFish("سمك قاروص", 10m, 50m);
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 11");
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-PAY1",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 100m // 100 x 50 = 5000 EGP
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            Assert.Equal(InvoicePaymentStatus.Unpaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(draftId));

            var firstTx = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = company.Id,
                PaymentDate = DateTime.Today,
                Amount = 2000m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = draftId, AllocatedAmount = 2000m }
                }
            });
            Assert.True(firstTx > 0);

            var remainingAfterFirst = await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(draftId);
            Assert.Equal(3000m, remainingAfterFirst);
            Assert.Equal(InvoicePaymentStatus.PartiallyPaid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(draftId));

            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = company.Id,
                PaymentDate = DateTime.Today,
                Amount = 3000m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = draftId, AllocatedAmount = 3000m }
                }
            });

            Assert.Equal(0m, await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(draftId));
            Assert.Equal(InvoicePaymentStatus.Paid, await _harness.InvoiceService.GetInvoicePaymentStatusAsync(draftId));
        }

        [Fact]
        public async Task Payment_CannotCrossPartyTypes()
        {
            var fish = _harness.SeedFrozenFish("سمك بلطي صغير", 10m, 10m);
            var wholesaleCustomer = _harness.SeedWholesaleCustomer("عميل جملة 2");
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 12");
            await _harness.CreateStockInAsync(fish.Id, 20m);

            var maritimeDraft = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-X1",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = maritimeDraft,
                ProductId = fish.Id,
                Quantity = 10m
            });
            await _harness.InvoiceService.PostInvoiceAsync(maritimeDraft);

            var wholesaleDraft = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = wholesaleCustomer.Id
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = wholesaleDraft,
                ProductId = fish.Id,
                Quantity = 10m,
                UnitPrice = 15m
            });
            await _harness.InvoiceService.PostInvoiceAsync(wholesaleDraft);

            // Wholesale customer cash landing on a maritime supply invoice: rejected.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
                {
                    CustomerId = wholesaleCustomer.Id,
                    PaymentDate = DateTime.Today,
                    Amount = 100m,
                    Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = maritimeDraft, AllocatedAmount = 100m } }
                }));

            // Maritime company cash landing on a wholesale invoice: rejected.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
                {
                    CustomerId = company.Id,
                    PaymentDate = DateTime.Today,
                    Amount = 100m,
                    Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = wholesaleDraft, AllocatedAmount = 100m } }
                }));
        }

        [Fact]
        public async Task CustomerBalance_FollowsLedgerFormula()
        {
            // Balance = OpeningBalance + PostedSalesInvoices - PostedCreditNotes - CashPayments + CashRefunds
            var fish = _harness.SeedFrozenFish("سمك فسيخ", 10m, 20m);
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 13");
            await _harness.CreateStockInAsync(fish.Id, 100m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-B13",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 50m // 50 x 20 = 1000 EGP
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            Assert.Equal(1000m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(company.Id));

            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = company.Id,
                PaymentDate = DateTime.Today,
                Amount = 400m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = draftId, AllocatedAmount = 400m } }
            });
            Assert.Equal(600m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(company.Id));

            var noteId = await _harness.CreditNoteService.CreateDraftCreditNoteAsync(new CreateCreditNoteDraftDto
            {
                OriginalInvoiceId = draftId,
                TransactionDate = DateTime.Today,
                Reason = "مرتجع كميات",
                Items = new List<CreateCreditNoteItemDto> { new() { ProductId = fish.Id, Quantity = 30m, UnitPrice = 20m } }
            });
            await _harness.CreditNoteService.PostCreditNoteAsync(new PostCreditNoteDto { CreditNoteId = noteId });

            // 1000 - 400 - 600(30kg x 20) = 0
            Assert.Equal(0m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(company.Id));
        }

        [Fact]
        public async Task Payment_ExceedingOutstanding_Throws()
        {
            var fish = _harness.SeedFrozenFish("سمك فارونا", 10m, 10m);
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 14");
            await _harness.CreateStockInAsync(fish.Id, 5m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-N14",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 10m // 100 EGP
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
                {
                    CustomerId = company.Id,
                    PaymentDate = DateTime.Today,
                    Amount = 150m,
                    Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = draftId, AllocatedAmount = 150m } }
                }));
        }

        [Fact]
        public async Task CreditNote_CannotExceedInvoiceQuantity_Throws()
        {
            var fish = _harness.SeedFrozenFish("سمك بوذي", 10m, 10m);
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 15");
            await _harness.CreateStockInAsync(fish.Id, 5m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-R15",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 10m
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.CreditNoteService.CreateDraftCreditNoteAsync(new CreateCreditNoteDraftDto
                {
                    OriginalInvoiceId = draftId,
                    TransactionDate = DateTime.Today,
                    Reason = "مرتجع زائد",
                    Items = new List<CreateCreditNoteItemDto> { new() { ProductId = fish.Id, Quantity = 25m, UnitPrice = 10m } }
                }));
        }

        public void Dispose() => _harness.Dispose();
    }
}
