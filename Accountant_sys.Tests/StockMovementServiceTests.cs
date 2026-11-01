using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Invoice.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Frozen fish inventory ledger tests: carton x weight math, withdrawal
    // decrements, negative-stock rejection, and draft-invoice isolation.
    public class StockMovementServiceTests : IDisposable
    {
        private readonly TestHarness _harness;

        public StockMovementServiceTests()
        {
            _harness = new TestHarness();
        }

        [Fact]
        public async Task StockIn_100CartonsX10Kg_Records1000Kg()
        {
            var fish = _harness.SeedFrozenFish("بلطي مجمد", 10m);

            await _harness.StockService.RecordStockInAsync(new CreateStockInDto
            {
                ProductId = fish.Id,
                Cartons = 100m,
                Notes = "استلام شحنة"
            });

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.NotNull(balance);
            Assert.Equal(100m, balance!.AvailableCartons);
            Assert.Equal(1000m, balance.AvailableKg);
        }

        [Fact]
        public async Task StockIn_MixedCartonWeights_AccumulatesBothDimensions()
        {
            var fish = _harness.SeedFrozenFish("روبي مجمد", 10m);

            await _harness.StockService.RecordStockInAsync(new CreateStockInDto { ProductId = fish.Id, Cartons = 10m });
            // Same product received later in heavier cartons (explicit override).
            await _harness.StockService.RecordStockInAsync(new CreateStockInDto { ProductId = fish.Id, Cartons = 5m, WeightPerCartonKg = 12m });

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(15m, balance!.AvailableCartons);
            Assert.Equal(160m, balance.AvailableKg); // 10x10 + 5x12
        }

        [Fact]
        public async Task StockOut_DecrementsWeightAndDerivesFractionalCartons()
        {
            var fish = _harness.SeedFrozenFish("ساردين مجمد", 10m);
            await _harness.CreateStockInAsync(fish.Id, 10m);

            await _harness.StockService.RecordStockOutAsync(new CreateStockOutDto
            {
                ProductId = fish.Id,
                WeightKg = 25m,
                Reason = "تالف"
            });

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(75m, balance!.AvailableKg);
            Assert.Equal(7.5m, balance.AvailableCartons); // fractional cartons derived from weight
        }

        [Fact]
        public async Task StockOut_MoreThanAvailable_Throws()
        {
            var fish = _harness.SeedFrozenFish("مكرونة سمك", 10m);
            await _harness.CreateStockInAsync(fish.Id, 2m); // 20 kg only

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordStockOutAsync(new CreateStockOutDto
                {
                    ProductId = fish.Id,
                    WeightKg = 20.001m,
                    Reason = "سحب يدوي"
                }));
        }

        [Fact]
        public async Task StockIn_NonFrozenProduct_Throws()
        {
            var rice = _harness.SeedRegularProduct("رز");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordStockInAsync(new CreateStockInDto { ProductId = rice.Id, Cartons = 5m }));
        }

        [Fact]
        public async Task StockIn_MissingCartonWeight_Throws()
        {
            var fish = _harness.SeedFrozenFish("كاليماري مجمد", 0m); // no default weight

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordStockInAsync(new CreateStockInDto { ProductId = fish.Id, Cartons = 5m }));
        }

        [Fact]
        public async Task Adjustment_DecreaseApplied_ButBelowZeroRejected()
        {
            var fish = _harness.SeedFrozenFish("ملوخية سمك", 10m);
            await _harness.CreateStockInAsync(fish.Id, 8m); // 80 kg

            // Physical count found 5 kg missing.
            await _harness.StockService.RecordAdjustmentAsync(new CreateStockAdjustmentDto
            {
                ProductId = fish.Id,
                WeightKgAdjustment = -5m,
                Reason = "تسوية جرد فعلي"
            });

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(75m, balance!.AvailableKg);

            // Pushing weight below zero must fail.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordAdjustmentAsync(new CreateStockAdjustmentDto
                {
                    ProductId = fish.Id,
                    WeightKgAdjustment = -100m,
                    Reason = "خطأ جرد"
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.StockService.RecordAdjustmentAsync(new CreateStockAdjustmentDto
                {
                    ProductId = fish.Id,
                    WeightKgAdjustment = -5m,
                    Reason = "" // reason is mandatory
                }));
        }

        [Fact]
        public async Task DraftMaritimeInvoice_DoesNotTouchStock()
        {
            var fish = _harness.SeedFrozenFish("بوري مجمد", 10m, 50m);
            var company = _harness.SeedMaritimeCompany("شركة البحر الأحمر");
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "SO-100",
                SupplyOrderDate = DateTime.Today
            });

            Assert.True(draftId > 0);

            var added = await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 100m // full stock — still only a draft
            });
            Assert.True(added);

            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(100m, balance!.AvailableKg);
            Assert.Contains(_harness.Context.StockMovements.Where(m => m.ProductId == fish.Id).ToList(),
                m => m.MovementType == StockMovementType.In && m.TotalWeightKg == 100m);
            Assert.Equal(1, _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id));
        }

        public void Dispose() => _harness.Dispose();
    }
}
