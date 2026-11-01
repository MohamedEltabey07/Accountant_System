using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Invoice.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Frozen stock ledger invariants: the movement ledger is the single source
    // of truth, so the summed ledger must never go negative — manual stock-out,
    // physical adjustments, and maritime dispatches all revalidate against the
    // available balance before persisting.
    public class FrozenStockInvariantTests : IDisposable
    {
        private readonly TestHarness _harness;

        public FrozenStockInvariantTests()
        {
            _harness = new TestHarness();
        }

        private static decimal LedgerBalanceKg(TestHarness h, int productId) =>
            h.Context.StockMovements.Where(m => m.ProductId == productId).Sum(m => m.TotalWeightKg);

        private static decimal LedgerBalanceCartons(TestHarness h, int productId) =>
            h.Context.StockMovements.Where(m => m.ProductId == productId).Sum(m => m.Cartons);

        [Fact]
        public async Task FullLifecycle_LedgerStaysNonNegative_AndMatchesDerivedBalance()
        {
            // In 100 kg → Out 25 kg → Adjustment -5 kg → Maritime dispatch 30 kg →
            // cancel the dispatch (reversal +30 kg).
            var fish = _harness.SeedFrozenFish("سمك دريسة مجمد", 10m, 20m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            await _harness.StockService.RecordStockOutAsync(new CreateStockOutDto
            {
                ProductId = fish.Id,
                WeightKg = 25m,
                Reason = "سحب تالف"
            });

            await _harness.StockService.RecordAdjustmentAsync(new CreateStockAdjustmentDto
            {
                ProductId = fish.Id,
                WeightKgAdjustment = -5m,
                Reason = "تسوية جرد"
            });

            var company = _harness.SeedMaritimeCompany("شركة البحر 20");
            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-INV-20", 30m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            // 100 - 25 - 5 - 30 = 40 kg before cancellation.
            Assert.Equal(40m, LedgerBalanceKg(_harness, fish.Id));
            Assert.Equal(40m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            await _harness.InvoiceService.CancelPostedInvoiceAsync(new BLL.DTOs.Invoice.InComingData.CancelInvoiceDto
            {
                InvoiceId = draftId,
                CancellationReason = "إلغاء أمر التوريد للاختبار"
            });

            // The dispatch movement (-30) stays in the ledger; cancellation appends
            // an auditable reversal (+30). Final: 100 - 25 - 5 - 30 + 30 = 70 kg.
            Assert.Equal(70m, LedgerBalanceKg(_harness, fish.Id));
            Assert.Equal(70m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Invariant: cartons balance is 10 (in) - 2.5 (out) - 3 (dispatch) + 3 (reversal)
            // = 7.5. Cartons*10 ≠ kg because the -5 kg adjustment carries no cartons.
            Assert.Equal(7.5m, LedgerBalanceCartons(_harness, fish.Id));
            Assert.All(
                _harness.Context.StockMovements.Where(m => m.ProductId == fish.Id).ToList(),
                m => Assert.True(m.TotalWeightKg >= -100m));

            // Final running balance never negative at any point of the lifecycle.
            Assert.True(LedgerBalanceKg(_harness, fish.Id) >= 0m);
        }

        [Fact]
        public async Task MaritimePosting_WithoutEnoughStock_Throws_AndLeavesNoNegativeOutMovement()
        {
            var fish = _harness.SeedFrozenFish("سمك كنعد مجمد", 10m, 25m);
            var company = _harness.SeedMaritimeCompany("شركة البحر 21");
            await _harness.CreateStockInAsync(fish.Id, 1m); // 10 kg only

            var movementsBefore = _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id);

            var draftId = await _harness.CreateMaritimeDraftAsync(company.Id, fish.Id, "SO-INV-21", 50m);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.PostInvoiceAsync(draftId));

            // Ledger must be untouched — no negative out movement persisted, invoice still in Draft.
            Assert.Equal(movementsBefore, _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id));
            Assert.Equal(10m, LedgerBalanceKg(_harness, fish.Id));
            Assert.Equal(InvoiceStatus.Draft, _harness.Context.Invoices.Single(i => i.Id == draftId).Status);
        }

        [Fact]
        public async Task SellingDownToExactlyZero_Works_ThenMoreSelling_Throws()
        {
            var fish = _harness.SeedFrozenFish("سمك جمبري مجمد", 10m, 15m);
            await _harness.CreateStockInAsync(fish.Id, 2m); // 20 kg

            await _harness.StockService.RecordStockOutAsync(new CreateStockOutDto
            {
                ProductId = fish.Id,
                WeightKg = 20m,
                Reason = "تصريف آخر الموجوديات"
            });
            Assert.Equal(0m, LedgerBalanceKg(_harness, fish.Id));
            Assert.Equal(0m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Any further issue without a positive adjustment must be rejected.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordStockOutAsync(new CreateStockOutDto
                {
                    ProductId = fish.Id,
                    WeightKg = 0.001m,
                    Reason = "محاولة سحب من صفر"
                }));

            // Explicit adjustment of zero-weight stock downward is still rejected too.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordAdjustmentAsync(new CreateStockAdjustmentDto
                {
                    ProductId = fish.Id,
                    WeightKgAdjustment = -1m,
                    Reason = "محاولة خصم من صفر"
                }));
        }

        public void Dispose() => _harness.Dispose();
    }
}
