using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Additional charges/expenses on maritime supply orders: billable charges are
    // added to the posted order total, internal-only charges are tracked but never
    // billed, amounts are always recalculated on the server, and adding charge rows
    // never creates cash movements of any kind.
    public class InvoiceAdditionalChargeTests : IDisposable
    {
        private readonly TestHarness _harness;

        public InvoiceAdditionalChargeTests()
        {
            _harness = new TestHarness();
        }

        public void Dispose() => _harness.Dispose();

        private async Task<int> CreateDraftAsync(int companyId, string orderNo, decimal kg = 100m)
        {
            var fish = _harness.SeedFrozenFish("سمك أمر المصاريف", 10m, 50m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg for posting
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = companyId,
                ExternalOrderNumber = orderNo,
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = kg, // 100 kg x 50 = 5,000
                IsFromFreezer = true
            });
            return draftId;
        }

        [Fact]
        public async Task NoCharges_TotalIsPurelyItems()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 0");
            var draftId = await CreateDraftAsync(company.Id, "CH-0");

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);

            Assert.Equal(5000m, invoice!.TotalAmount);
            Assert.Equal(0m, invoice.BillableChargesTotal);
            Assert.Equal(0m, invoice.InternalChargesTotal);
            Assert.Empty(invoice.AdditionalCharges);
        }

        [Fact]
        public async Task BillableCharge_QuantityTimesPrice_IsRecalculatedServerSide_IncludedInTotal()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 1");
            var draftId = await CreateDraftAsync(company.Id, "CH-1");

            // Amount is sent wrong from the client; the server recomputes 10 x 5 = 50.
            var added = await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "صناديق ثلج",
                Quantity = 10m,
                UnitPrice = 5m,
                Amount = 999m,
                IsBillableToCompany = true
            });

            Assert.True(added);
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);

            var charge = invoice!.AdditionalCharges.Single();
            Assert.Equal(50m, charge.Amount);
            Assert.Equal(50m, invoice.BillableChargesTotal);
            Assert.Equal(5050m, invoice.TotalAmount); // 5,000 + 50 - no discounts
        }

        [Fact]
        public async Task DirectAmountCharge_WithoutQuantityPrice_IsAccepted()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 2");
            var draftId = await CreateDraftAsync(company.Id, "CH-2");

            var added = await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "نقل للمرسى",
                Amount = 300m
            });

            Assert.True(added);
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(300m, invoice!.AdditionalCharges.Single().Amount);
            Assert.Equal(5300m, invoice.TotalAmount);
        }

        [Fact]
        public async Task InternalCharge_TrackedButNeverBilled()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 3");
            var draftId = await CreateDraftAsync(company.Id, "CH-3");

            var added = await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "استقبال داخلي",
                Amount = 200m,
                IsBillableToCompany = false
            });

            Assert.True(added);
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(0m, invoice!.BillableChargesTotal);
            Assert.Equal(200m, invoice.InternalChargesTotal);
            Assert.Equal(5000m, invoice.TotalAmount); // internal charge never billed
        }

        [Fact]
        public async Task MixedCharges_BillableCountedOnce_InternalExcluded()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 4");
            var draftId = await CreateDraftAsync(company.Id, "CH-4");

            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "ثلج 10 صناديق",
                Quantity = 10m,
                UnitPrice = 5m,
                IsBillableToCompany = true
            });
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "عمالة شحن داخلية",
                Amount = 150m,
                IsBillableToCompany = false
            });

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(2, invoice!.AdditionalCharges.Count);
            Assert.Equal(5050m, invoice.TotalAmount); // billable 50 exactly once
            Assert.Equal(50m, invoice.BillableChargesTotal);
            Assert.Equal(150m, invoice.InternalChargesTotal);
        }

        [Fact]
        public async Task Charges_SurvivePosting_InPostedTotal()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 5");
            var draftId = await CreateDraftAsync(company.Id, "CH-5");
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "غرامة تأخير",
                Amount = 75m,
                IsBillableToCompany = true
            });

            var posted = await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.True(posted);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(InvoiceStatus.Posted, invoice!.Status);
            var charge = invoice.AdditionalCharges.Single();
            Assert.Equal("غرامة تأخير", charge.Description);
            Assert.Equal(75m, charge.Amount);
            Assert.Equal(5075m, invoice.TotalAmount);
        }

        [Fact]
        public async Task AddingCharge_NeverCreatesAnyCashMovement()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 6");
            var draftId = await CreateDraftAsync(company.Id, "CH-6");
            await _harness.CreateStockInAsync(_harness.Context.InvoiceItems
                .Where(i => i.InvoiceId == draftId).Select(i => i.ProductId).Single(), 20m);

            var beforeCount = _harness.Context.CashTransactions.Count();
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "مصروف ورقي",
                Amount = 30m
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            var afterCount = _harness.Context.CashTransactions.Count();

            Assert.Equal(beforeCount, afterCount); // charges are bookkeeping only
        }

        [Fact]
        public async Task RemoveCharge_RestoresPureItemTotal()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 7");
            var draftId = await CreateDraftAsync(company.Id, "CH-7");
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "مصروف للحذف",
                Amount = 60m
            });
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            var chargeId = invoice!.AdditionalCharges.Single().Id;

            var removed = await _harness.InvoiceService.RemoveChargeFromDraftAsync(chargeId);
            Assert.True(removed);

            invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Empty(invoice!.AdditionalCharges);
            Assert.Equal(5000m, invoice.TotalAmount);
        }

        [Fact]
        public async Task UpdateCharge_ReplacesAllValues()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 8");
            var draftId = await CreateDraftAsync(company.Id, "CH-8");
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "ثلج قديم",
                Amount = 40m
            });
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            var chargeId = invoice!.AdditionalCharges.Single().Id;

            var updated = await _harness.InvoiceService.UpdateDraftChargeAsync(new UpdateInvoiceChargeDto
            {
                ChargeId = chargeId,
                InvoiceId = draftId,
                Description = "ثلج 8 صناديق",
                Quantity = 8m,
                UnitPrice = 6m, // 48
                IsBillableToCompany = true,
                Notes = "محسوبة على الصناديق"
            });

            Assert.True(updated);
            invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            var charge = invoice!.AdditionalCharges.Single();
            Assert.Equal("ثلج 8 صناديق", charge.Description);
            Assert.Equal(48m, charge.Amount);
            Assert.Equal(5048m, invoice.TotalAmount);
        }

        [Fact]
        public async Task Charges_AreMaritimeDraftOnly()
        {
            // Non-maritime draft: charge creation is rejected.
            var customer = _harness.SeedWholesaleCustomer("عميل جملة مصاريف");
            var wholesaleDraft = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.AddChargeToDraftAsync(
                new AddInvoiceChargeDto { InvoiceId = wholesaleDraft, Description = "مصروف مرفوض", Amount = 10m }));

            // Posted maritime order: charge creation returns false (not a draft).
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 9");
            var draftId = await CreateDraftAsync(company.Id, "CH-9");
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            var posted = await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "مصروف مرحل مرفوض",
                Amount = 10m
            });
            Assert.False(posted);
        }

        [Fact]
        public async Task IncompleteChargeData_IsRejected()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 10");
            var draftId = await CreateDraftAsync(company.Id, "CH-10");

            // Quantity without unit price and without a direct amount.
            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.AddChargeToDraftAsync(
                new AddInvoiceChargeDto { InvoiceId = draftId, Description = "غير مكتمل", Quantity = 5m }));

            // Zero amount.
            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.AddChargeToDraftAsync(
                new AddInvoiceChargeDto { InvoiceId = draftId, Description = "صفر", Amount = 0m }));

            // Empty description.
            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.AddChargeToDraftAsync(
                new AddInvoiceChargeDto { InvoiceId = draftId, Description = "   ", Amount = 10m }));
        }

        [Fact]
        public async Task DetailedStatement_IncludesChargesPerOrder()
        {
            var company = _harness.SeedMaritimeCompany("شركة مصاريف 11");
            var draftId = await CreateDraftAsync(company.Id, "CH-11");
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "ثلج كشف تفصيلي",
                Quantity = 4m,
                UnitPrice = 5m,
                IsBillableToCompany = true
            });
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "داخلي للكشف",
                Amount = 20m,
                IsBillableToCompany = false
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var orders = await _harness.InvoiceService.GetMaritimeSupplyOrdersAsync(company.Id, null, null);
            var order = orders.Single();

            Assert.Equal(2, order.Charges.Count);
            Assert.True(order.HasCharges);
            Assert.Equal(20m, order.BillableChargesTotal);
            Assert.Equal(20m, order.InternalChargesTotal);
            // The order total shown on statements already contains the billable charge.
            Assert.Equal(5020m, order.TotalAmount);
        }
    }
}
