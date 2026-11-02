using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Per-item freezer sourcing on maritime supply orders: the supply source
    // is decided per invoice row (never inferred from the product category),
    // only freezer-sourced rows are validated/deducted/returned from stock,
    // and draft operations never touch the ledger.
    public class FreezerSourcingTests : IDisposable
    {
        private readonly TestHarness _harness;

        public FreezerSourcingTests()
        {
            _harness = new TestHarness();
        }

        [Fact]
        public async Task FrozenItem_FromFreezer_Posts_CreatesOutMovement()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 1");
            var fish = _harness.SeedFrozenFish("سمك من الثلاجة", 10m, 40m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-1", 25m, isFromFreezer: true);
            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);

            Assert.True(posted);
            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(75m, balance!.AvailableKg); // 100 - 25

            var outMovement = _harness.Context.StockMovements
                .Single(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.Out);
            Assert.Equal(-25m, outMovement.TotalWeightKg);
            Assert.Equal(draftId, outMovement.SourceInvoiceId);
        }

        [Fact]
        public async Task RegularItem_NotFromFreezer_Accepted_NoValidation_NoMovement()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 2");
            var rice = _harness.SeedRegularProduct("رز عادي للسفن");

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "FS-2",
                SupplyOrderDate = DateTime.Today
            });

            // A regular product with NO freezer existence at all is accepted:
            // explicit UnitPrice (no maritime default price on regular products).
            var added = await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = rice.Id,
                Quantity = 50m,
                UnitPrice = 100m,
                IsFromFreezer = false
            });
            Assert.True(added);

            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.True(posted);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(5000m, invoice!.TotalAmount); // 50 x 100 — normal calculation
            Assert.False(invoice.Items.Single().IsFromFreezer);

            // No freezer movements of any kind were created for the item.
            Assert.Empty(_harness.Context.StockMovements.Where(m => m.ProductId == rice.Id));
        }

        [Fact]
        public async Task FrozenItem_NotFromFreezer_Posts_FreezerStockUnchanged()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 3");
            var fish = _harness.SeedFrozenFish("سمك المجمد الخارجي", 10m, 30m);
            await _harness.CreateStockInAsync(fish.Id, 5m); // 50 kg available

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-3", 45m, isFromFreezer: false);
            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);

            // Allowed: classified frozen, but this order acquires it elsewhere.
            Assert.True(posted);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.False(invoice!.Items.Single().IsFromFreezer);

            var outRows = _harness.Context.StockMovements
                .Count(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.Out);
            Assert.Equal(0, outRows);
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Beyond available stock it STILL posts without touching the ledger.
            var draft2 = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "FS-3b",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draft2,
                ProductId = fish.Id,
                Quantity = 500m, // far beyond the 50 kg available
                IsFromFreezer = false
            });
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draft2));
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task InsufficientFreezerStock_Throws_NoPartialWrites()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 4");
            var fish = _harness.SeedFrozenFish("سمك ناقص", 10m, 30m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "FS-4",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 150m,
                IsFromFreezer = true
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.PostInvoiceAsync(draftId));

            // No partial database writes: invoice stays Draft and no ledger row exists.
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(InvoiceStatus.Draft, invoice!.Status);
            Assert.Empty(_harness.Context.StockMovements.Where(m => m.MovementType == StockMovementType.Out));
        }

        [Fact]
        public async Task MixedOrder_OnlyFreezerItemsAffectStock_AllItemsInTotal()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 5");
            var fishA = _harness.SeedFrozenFish("سمك A", 10m, 100m);
            var fishB = _harness.SeedFrozenFish("سمك B", 10m, 80m);
            var rice = _harness.SeedRegularProduct("بضاعة عادية");
            await _harness.CreateStockInAsync(fishA.Id, 20m); // 200 kg
            await _harness.CreateStockInAsync(fishB.Id, 5m);  // 50 kg

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "FS-5",
                SupplyOrderDate = DateTime.Today
            });

            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId, ProductId = fishA.Id, Quantity = 200m, IsFromFreezer = true
            }); // exactly all available stock
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId, ProductId = fishB.Id, Quantity = 45m, UnitPrice = 80m, IsFromFreezer = false
            }); // freezer stock is NOT inspected for a non-freezer item (45 <= 50 anyway)
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId, ProductId = rice.Id, Quantity = 10m, UnitPrice = 250m, IsFromFreezer = false
            });

            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.True(posted);

            // Stock: only the explicitly freezer-sourced item shifted the ledger.
            Assert.Equal(0m, (await _harness.StockService.GetBalanceAsync(fishA.Id))!.AvailableKg);
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fishB.Id))!.AvailableKg);

            // Total includes all three items regardless of the supply source.
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(200m * 100m + 45m * 80m + 10m * 250m, invoice!.TotalAmount);
            Assert.Equal(3, invoice.Items.Count);
            Assert.Equal(1, invoice.Items.Count(i => i.IsFromFreezer));
        }

        [Fact]
        public async Task DraftSaving_NeverReducesFreezerStock()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 6");
            var fish = _harness.SeedFrozenFish("سمك مسودة", 10m, 40m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-6", 80m, isFromFreezer: true);

            Assert.Equal(100m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
            Assert.Empty(_harness.Context.StockMovements.Where(m => m.MovementType == StockMovementType.Out));
        }

        [Fact]
        public async Task DuplicateDispatch_DeductsStockOnlyOnce()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 7");
            var fish = _harness.SeedFrozenFish("سمك مكرر", 10m, 30m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-7", 40m, isFromFreezer: true);
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            Assert.False(await _harness.InvoiceService.PostInvoiceAsync(draftId));

            var outRows = _harness.Context.StockMovements
                .Count(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.Out);
            Assert.Equal(1, outRows);
            Assert.Equal(60m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task FreezerSource_TogglingOnDraft_ReflectsInPosting()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 8");
            var fish = _harness.SeedFrozenFish("سمك قابل للتبديل", 10m, 35m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-8", 40m, isFromFreezer: true);

            // Un-tag while still a draft: the item become freezer-less.
            var item = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            var itemId = item!.Items.Single().Id;
            await _harness.InvoiceService.UpdateDraftItemAsync(new UpdateInvoiceItemDto
            {
                ItemId = itemId,
                Quantity = 40m,
                UnitPrice = 35m,
                LineDiscountAmount = 0,
                IsFromFreezer = false
            });

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            Assert.Empty(_harness.Context.StockMovements.Where(m => m.MovementType == StockMovementType.Out));
            Assert.Equal(100m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task FreezerSource_OnNonMaritimeDraft_Throws()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل جملة غير بحري");
            var fish = _harness.SeedFrozenFish("سمك جملة", 10m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = fish.Id,
                    Quantity = 5m,
                    UnitPrice = 10m,
                    IsFromFreezer = true
                }));
        }

        [Fact]
        public async Task InactiveProduct_CannotBeAddedToAnyDraft()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل جملة التجميد");
            var fish = _harness.SeedFrozenFish("سمك مجمد بديل", 10m);
            await _harness.ProductService.ToggleStatusAsync(fish.Id); // deactivate

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = fish.Id,
                    Quantity = 5m
                }));
        }

        [Fact]
        public async Task CreditNoteOnNonFreezerItem_DoesNotReturnFishToFreezer()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 9");
            var fish = _harness.SeedFrozenFish("سمك خارجي", 10m, 30m);
            await _harness.CreateStockInAsync(fish.Id, 5m); // 50 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-9", 45m, isFromFreezer: false);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var noteId = await _harness.CreditNoteService.CreateDraftCreditNoteAsync(new BLL.DTOs.CreditNote.InComingData.CreateCreditNoteDraftDto
            {
                OriginalInvoiceId = draftId,
                TransactionDate = DateTime.Today,
                Reason = "مرتجع خارج الثلاجة",
                Items = new List<BLL.DTOs.CreditNote.InComingData.CreateCreditNoteItemDto>
                {
                    new() { ProductId = fish.Id, Quantity = 10m, UnitPrice = 30m }
                }
            });

            await _harness.CreditNoteService.PostCreditNoteAsync(new BLL.DTOs.CreditNote.InComingData.PostCreditNoteDto { CreditNoteId = noteId });

            // The return never enters the freezer: the original item was not sourced from it.
            var inMovements = _harness.Context.StockMovements
                .Where(m => m.ProductId == fish.Id && m.SourceInvoiceId == draftId)
                .ToList();
            Assert.Empty(inMovements);
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Financially the credit note still reduces the customer balance.
            var customerBalance = await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(company.Id);
            Assert.Equal(1350m - 300m, customerBalance);
        }

        [Fact]
        public async Task CreditNoteOnFreezerItem_StillReturnsFishToFreezer()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصدر 10");
            var fish = _harness.SeedFrozenFish("سمك مرحّع", 10m, 25m);
            await _harness.CreateStockInAsync(fish.Id, 5m); // 50 kg

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "FS-10", 45m, isFromFreezer: true);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.Equal(5m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            var noteId = await _harness.CreditNoteService.CreateDraftCreditNoteAsync(new BLL.DTOs.CreditNote.InComingData.CreateCreditNoteDraftDto
            {
                OriginalInvoiceId = draftId,
                TransactionDate = DateTime.Today,
                Reason = "مرتجع من الثلاجة",
                Items = new List<BLL.DTOs.CreditNote.InComingData.CreateCreditNoteItemDto>
                {
                    new() { ProductId = fish.Id, Quantity = 10m, UnitPrice = 25m }
                }
            });

            await _harness.CreditNoteService.PostCreditNoteAsync(new BLL.DTOs.CreditNote.InComingData.PostCreditNoteDto { CreditNoteId = noteId });

            var returnMovement = _harness.Context.StockMovements
                .Single(m => m.ProductId == fish.Id && m.MovementType == StockMovementType.In && m.SourceInvoiceId == draftId);
            Assert.Equal(10m, returnMovement.TotalWeightKg);
            Assert.Equal(15m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        public void Dispose() => _harness.Dispose();
    }
}
