using Accountant_sys.Tests.Helpers;
using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.Invoice.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Maritime supply order tests: party/type compatibility, external order
    // number rules, atomic stock deduction on posting, and auditable cancel.
    public class MaritimeInvoiceTests : IDisposable
    {
        private readonly TestHarness _harness;

        public MaritimeInvoiceTests()
        {
            _harness = new TestHarness();
        }

        [Fact]
        public async Task MaritimeDraft_RequiresExternalOrderNumber_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
                {
                    Type = InvoiceType.MaritimeSupply,
                    TransactionDate = DateTime.Today,
                    CustomerId = _harness.SeedMaritimeCompany("شركة ملاحة 1").Id,
                    ExternalOrderNumber = null,
                    SupplyOrderDate = null
                }));
        }

        [Fact]
        public async Task MaritimeDraft_DuplicateExternalOrderNumber_Throws()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 2");
            var fish = _harness.SeedFrozenFish("فيليه عروس البحر", 10m, 40m);
            await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-777", 10m);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-777", 5m));
        }

        [Fact]
        public async Task MaritimeInvoice_ForWholesaleCustomer_Throws_AndViceVersa()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل جملة");
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 3");

            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.CreateMaritimeDraftAsync(customer.Id, 1, "SO-A", 5m));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
                {
                    Type = InvoiceType.WholesaleSales,
                    TransactionDate = DateTime.Today,
                    CustomerId = company.Id
                }));
        }

        [Fact]
        public async Task MaritimeDraft_NonFrozenProduct_Throws()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 4");
            var rice = _harness.SeedRegularProduct("رز عادي");
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-B",
                SupplyOrderDate = DateTime.Today
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = rice.Id,
                    Quantity = 5m
                }));
        }

        [Fact]
        public async Task MaritimePost_CreatesOutMovements_DecrementsStock()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 5");
            var fish = _harness.SeedFrozenFish("سمك بلطي نص ملكي", 10m, 50m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-900", 40m);
            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);

            Assert.True(posted);

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(60m, balance!.AvailableKg);

            var movements = _harness.Context.StockMovements.Where(m => m.ProductId == fish.Id).ToList();
            var outMovement = movements.Single(m => m.MovementType == StockMovementType.Out);
            Assert.Equal(-40m, outMovement.TotalWeightKg);
            Assert.Equal(-4m, outMovement.Cartons);
            Assert.Equal(draftId, outMovement.SourceInvoiceId);
        }

        [Fact]
        public async Task MaritimePost_UsesDefaultPricePerKg_AndMARPrefix()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 6");
            var fish = _harness.SeedFrozenFish("سمك هامور", 10m, 75m);
            await _harness.CreateStockInAsync(fish.Id, 5m);

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-999", 20m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.NotNull(invoice);
            Assert.StartsWith("MAR-", invoice!.InvoiceNumber);
            Assert.Equal(1500m, invoice.TotalAmount); // 20 kg x 75 EGP
            Assert.Equal("SO-999", invoice.ExternalOrderNumber);
        }

        [Fact]
        public async Task MaritimePost_InsufficientStock_Throws_NoLedgerRows()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 7");
            var fish = _harness.SeedFrozenFish("سمك موسى", 10m, 30m);
            await _harness.CreateStockInAsync(fish.Id, 2m); // 20 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-801", 100m);
            var outMovementRowsBefore = _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.PostInvoiceAsync(draftId));

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.NotEqual(InvoiceStatus.Posted, invoice!.Status);

            var outMovementRows = _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.Out);
            Assert.Equal(0, outMovementRows);
            Assert.Equal(20m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg); // untouched
        }

        [Fact]
        public async Task MaritimePost_Twice_Blocked()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 8");
            var fish = _harness.SeedFrozenFish("سمك ناجل", 10m, 20m);
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-610", 30m);
            var first = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.True(first);

            var second = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.False(second);

            var outRows = _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.Out);
            Assert.Equal(1, outRows); // deducted once only
            Assert.Equal(70m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task CancelPostedMaritimeInvoice_CreatesReversals_RestoresStock()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 9");
            var fish = _harness.SeedFrozenFish("سمك صفيلح", 10m, 45m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-450", 70m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.Equal(30m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            var cancelled = await _harness.InvoiceService.CancelPostedInvoiceAsync(
                new BLL.DTOs.Invoice.InComingData.CancelInvoiceDto
                {
                    InvoiceId = draftId,
                    CancellationReason = "خطأ في وزن الأمر"
                });
            Assert.True(cancelled);

            // Original receipt row + original Out row stay, one reversal row added —
            // auditable, no ledger rows ever deleted.
            var movements = _harness.Context.StockMovements.Where(m => m.ProductId == fish.Id).ToList();
            Assert.Equal(3, movements.Count);
            var reversal = movements.Single(m => m.ReversalOfStockMovementId != null);
            Assert.Equal(70m, reversal.TotalWeightKg);
            var postedOut = movements.Single(m => m.ReversalOfStockMovementId == null && m.MovementType == StockMovementType.Out);
            Assert.Equal(postedOut.Id, reversal.ReversalOfStockMovementId);

            Assert.Equal(100m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task CreditNoteOnMaritimeInvoice_Posted_ReturnsFishToFreezer()
        {
            var company = _harness.SeedMaritimeCompany("شركة ملاحة 10");
            var fish = _harness.SeedFrozenFish("سمك فليتو", 10m, 25m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-320", 50m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            var noteId = await _harness.CreditNoteService.CreateDraftCreditNoteAsync(new CreateCreditNoteDraftDto
            {
                OriginalInvoiceId = draftId,
                TransactionDate = DateTime.Today,
                Reason = "مرتجع",
                Items = new List<CreateCreditNoteItemDto>
                {
                    new() { ProductId = fish.Id, Quantity = 20m, UnitPrice = 25m }
                }
            });

            await _harness.CreditNoteService.PostCreditNoteAsync(new PostCreditNoteDto { CreditNoteId = noteId });

            var movements = _harness.Context.StockMovements.Where(m => m.ProductId == fish.Id).ToList();
            var inMovements = movements.Where(m => m.MovementType == StockMovementType.In && m.SourceInvoiceId == draftId).ToList();
            Assert.Single(inMovements);
            Assert.Equal(20m, inMovements.Single().TotalWeightKg);
            Assert.Equal(70m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg); // 50 + 20 returned
        }

        public void Dispose() => _harness.Dispose();
    }
}
