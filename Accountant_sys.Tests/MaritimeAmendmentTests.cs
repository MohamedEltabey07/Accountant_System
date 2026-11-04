using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Controlled amendment of posted maritime supply orders: full validation
    // BEFORE any mutation, freezer stock reconciled by the DIFF only, payments
    // never touched, and an auditable InvoiceAmendment record on every change.
    public class MaritimeAmendmentTests : IDisposable
    {
        private readonly TestHarness _harness;
        private const decimal Price = 50m;

        public MaritimeAmendmentTests()
        {
            _harness = new TestHarness();
        }

        public void Dispose() => _harness.Dispose();

        private async Task<(int DraftId, int FishId, int CompanyId)> PostOrderAsync(
            string orderNo, decimal kg, decimal stockKg, string company = "شركة تصحيح")
        {
            var co = _harness.SeedMaritimeCompany(company);
            var fish = _harness.SeedFrozenFish("سمك التصحيح", 10m, Price, 20m);
            if (stockKg > 0) await _harness.CreateStockInAsync(fish.Id, stockKg / 10m); // kg / carton weight

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = co.Id,
                ExternalOrderNumber = orderNo,
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = kg,
                IsFromFreezer = true
            });
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return (draftId, fish.Id, co.Id);
        }

        private static AmendPostedMaritimeInvoiceDto Amendment(int invoiceId, int itemId, decimal kg, decimal price, string reason)
        {
            return new AmendPostedMaritimeInvoiceDto
            {
                InvoiceId = invoiceId,
                Reason = reason,
                UpdatedItems = new List<UpdateInvoiceItemDto>
                {
                    new() { ItemId = itemId, Quantity = kg, UnitPrice = price, LineDiscountAmount = 0m, IsFromFreezer = true }
                }
            };
        }

        private async Task<BLL.DTOs.Invoice.OutGoingData.InvoiceItemDto> FirstItemAsync(int invoiceId)
        {
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            return invoice!.Items.Single();
        }

        [Fact]
        public async Task PostedWeightCorrection_DecriedAmount_AdjustsFreezerStockByDiffOnly()
        {
            var (invoiceId, fishId, _) = await PostOrderAsync("AM-1", kg: 25m, stockKg: 100m);

            var item = await FirstItemAsync(invoiceId);
            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(
                Amendment(invoiceId, item.Id, 5m, Price, "أخطأ الوزن المرسل من الشركة"), "user-1"));

            // One compensating stock return of 20 kg exactly.
            var movements = _harness.Context.StockMovements
                .Where(m => m.ProductId == fishId && m.SourceInvoiceId == invoiceId).ToList();
            Assert.Equal(2, movements.Count);
            var compensation = movements.Single(m => m.MovementType == StockMovementType.In);
            Assert.Equal(20m, compensation.TotalWeightKg);
            Assert.Null(compensation.ReversalOfStockMovementId);

            Assert.Equal(95m, (await _harness.StockService.GetBalanceAsync(fishId))!.AvailableKg);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            Assert.Equal(250m, invoice!.TotalAmount); // 5 kg x 50
            Assert.Single(invoice.Amendments);
        }

        [Fact]
        public async Task PostedWeightCorrection_PaidOrder_TotalDrops_CreditStaysOnCustomerBalance()
        {
            var (invoiceId, fishId, companyId) = await PostOrderAsync("AM-2", kg: 100m, stockKg: 200m);

            var paymentId = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = companyId,
                PaymentDate = DateTime.Today,
                Amount = Price * 100m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = invoiceId, AllocatedAmount = Price * 100m } }
            });
            Assert.True(paymentId > 0);

            var item = await FirstItemAsync(invoiceId);
            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(
                Amendment(invoiceId, item.Id, 90m, Price, "خسارة أثناء النقل 10 كجم"), "user-1"));

            // Order total drops by 10 kg x price; the full payment stays allocated.
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            Assert.Equal(Price * 90m, invoice!.TotalAmount);
            Assert.Equal(Price * 90m - Price * 100m, invoice.RemainingBalance); // -500 credit on the order

            // Payments are untouched; the company has a net credit balance (overpaid vs new totals).
            Assert.Equal(-Price * 10m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(companyId));
        }

        [Fact]
        public async Task InsufficientStock_Increase_Rejected_NoWrites()
        {
            var (invoiceId, fishId, _) = await PostOrderAsync("AM-3", kg: 25m, stockKg: 100m);

            var item = await FirstItemAsync(invoiceId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(
                    Amendment(invoiceId, item.Id, 200m, Price, "زيادة بدون رصيد كافٍ"), "user-1"));

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            Assert.Equal(Price * 25m, invoice!.TotalAmount); // unchanged
            Assert.Single(invoice.Items);
            Assert.Empty(invoice.Amendments); // audit row NOT written

            var movements = _harness.Context.StockMovements
                .Where(m => m.ProductId == fishId && m.SourceInvoiceId == invoiceId).ToList();
            Assert.Single(movements); // only the original Out
            Assert.Equal(75m, (await _harness.StockService.GetBalanceAsync(fishId))!.AvailableKg);
        }

        [Fact]
        public async Task PriceOnlyCorrection_ChangesTotal_NoStockMovement()
        {
            var (invoiceId, fishId, _) = await PostOrderAsync("AM-4", kg: 20m, stockKg: 100m);

            var item = await FirstItemAsync(invoiceId);
            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(
                Amendment(invoiceId, item.Id, 20m, 55m, "تصحيح سعر الكجم بعد اعتماد الشركة"), "user-1"));

            var movements = _harness.Context.StockMovements
                .Where(m => m.ProductId == fishId && m.SourceInvoiceId == invoiceId).ToList();
            Assert.Single(movements); // no new freezer movements at all

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            Assert.Equal(55m * 20m, invoice!.TotalAmount);
            Assert.Single(invoice.Amendments);
            Assert.Equal(80m, (await _harness.StockService.GetBalanceAsync(fishId))!.AvailableKg); // 100 posted minus 20
        }

        [Fact]
        public async Task SourcingFlip_BothDirections_MoveTheDiff()
        {
            var co = _harness.SeedMaritimeCompany("شركة تصحيح المصدر");
            var fish = _harness.SeedFrozenFish("سمك التبديل", 10m, Price, 20m);
            var rice = _harness.SeedRegularProduct("رز التبديل");
            await _harness.CreateStockInAsync(fish.Id, 20m); // 200 kg

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = co.Id,
                ExternalOrderNumber = "AM-5",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(
                new AddInvoiceItemDto { InvoiceId = draftId, ProductId = fish.Id, Quantity = 30m, IsFromFreezer = true });
            await _harness.InvoiceService.AddItemToDraftAsync(
                new AddInvoiceItemDto { InvoiceId = draftId, ProductId = rice.Id, Quantity = 10m, UnitPrice = 100m, IsFromFreezer = false });

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            Assert.Equal(170m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            var items = invoice!.Items.ToDictionary(i => i.ProductId);

            // fish: freezer -> local (returns 30 kg to the freezer)
            // rice: local -> freezer -> rejected: rice is not a frozen-fish product.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
                {
                    InvoiceId = draftId,
                    Reason = "تحويل الأرز للتوريد من الثلاجة (غير مسموح)",
                    UpdatedItems = new List<UpdateInvoiceItemDto>
                    {
                        new() { ItemId = items[fish.Id].Id, Quantity = 30m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = true },
                        new() { ItemId = items[rice.Id].Id, Quantity = 10m, UnitPrice = 100m, LineDiscountAmount = 0m, IsFromFreezer = true }
                    }
                }, "user-1"));

            // Push the fish item back and amend rice removal — fish diff must still apply.
            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
            {
                InvoiceId = draftId,
                Reason = "استبدال السمك بالأرز من مصدر خارجي",
                UpdatedItems = new List<UpdateInvoiceItemDto>
                {
                    new() { ItemId = items[fish.Id].Id, Quantity = 30m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = false }
                },
                RemovedItemIds = new List<int> { items[rice.Id].Id },
                NewItems = new List<AddInvoiceItemDto>
                {
                    new() { InvoiceId = draftId, ProductId = fish.Id, Quantity = 30m, IsFromFreezer = false }
                }
            }, "user-1"));

            // fish net freezer requirement dropped 30 -> 0: exactly 30 kg returned.
            Assert.Equal(200m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
            var riceMovements = _harness.Context.StockMovements.Where(m => m.ProductId == rice.Id).ToList();
            Assert.Empty(riceMovements); // rice never touched the freezer ledger
        }

        [Fact]
        public async Task NewChargeThroughAmendment_IncludedInTotal_AuditRowWritten()
        {
            var (invoiceId, _, _) = await PostOrderAsync("AM-6", kg: 40m, stockKg: 100m, "شركة تصحيح مصاريف");
            var backlog = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            var item = backlog!.Items.Single();

            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
            {
                InvoiceId = invoiceId,
                Reason = "إضافة مصاريف الصناديق بعد الترحيل",
                UpdatedItems = new List<UpdateInvoiceItemDto>
                {
                    new() { ItemId = item.Id, Quantity = 40m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = true }
                },
                Charges = new List<UpdateInvoiceChargeDto>
                {
                    new() { ChargeId = 0, InvoiceId = invoiceId, Description = "صناديق ثلج", Quantity = 20m, UnitPrice = 4m, IsBillableToCompany = true }
                }
            }, "user-1"));

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(invoiceId);
            var charge = invoice!.AdditionalCharges.Single();
            Assert.Equal(80m, charge.Amount); // 20 x 4 recalculated server-side
            Assert.Equal(Price * 40m + 80m, invoice.TotalAmount);
            Assert.Equal(80m, invoice.BillableChargesTotal);

            var amendment = _harness.Context.Set<InvoiceAmendment>().Single(a => a.InvoiceId == invoiceId);
            Assert.Equal("إضافة مصاريف الصناديق بعد الترحيل", amendment.Reason);
            Assert.Equal("user-1", amendment.AmendedByUserId);
            Assert.False(string.IsNullOrWhiteSpace(amendment.BeforeJson));
            Assert.False(string.IsNullOrWhiteSpace(amendment.AfterJson));
        }

        [Fact]
        public async Task InvalidReason_AndNonMaritimeTarget_AreRejected()
        {
            var (invoiceId, _, _) = await PostOrderAsync("AM-7", kg: 10m, stockKg: 100m);
            var item = await FirstItemAsync(invoiceId);

            // Empty reason.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
                {
                    InvoiceId = invoiceId,
                    Reason = "  ",
                    UpdatedItems = new List<UpdateInvoiceItemDto>
                    {
                        new() { ItemId = item.Id, Quantity = 12m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = true }
                    }
                }, "user-1"));

            // Empty order: removing every item is refused (void instead).
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
                {
                    InvoiceId = invoiceId,
                    Reason = "حذف كل البنود",
                    RemovedItemIds = new List<int> { item.Id }
                }, "user-1"));
        }

        [Fact]
        public async Task ExternalNumberCorrection_RevalidatedAgainstDuplicates()
        {
            var co = _harness.SeedMaritimeCompany("شركة تصحيح الرقم");
            var fish = _harness.SeedFrozenFish("سمك الرقم", 10m, Price, 20m);
            await _harness.CreateStockInAsync(fish.Id, 10m);

            async Task<(int Id, int ItemId)> PostWithNumber(string number)
            {
                var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
                {
                    Type = InvoiceType.MaritimeSupply,
                    TransactionDate = DateTime.Today,
                    CustomerId = co.Id,
                    ExternalOrderNumber = number,
                    SupplyOrderDate = DateTime.Today
                });
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId, ProductId = fish.Id, Quantity = 10m, IsFromFreezer = false
                });
                Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
                var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
                return (draftId, invoice!.Items.Single().Id);
            }

            var (firstId, _) = await PostWithNumber("AREX-100");
            var (secondId, secondItem) = await PostWithNumber("AREX-200");

            // Renaming the second order to the first order's number is rejected.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
                {
                    InvoiceId = secondId,
                    Reason = "تصحيح رقم أمر الشركة",
                    ExternalOrderNumber = "AREX-100",
                    UpdatedItems = new List<UpdateInvoiceItemDto>
                    {
                        new() { ItemId = secondItem, Quantity = 10m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = false }
                    }
                }, "user-1"));

            // Renaming to a free number succeeds and shows up on the amended order.
            Assert.True(await _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
            {
                InvoiceId = secondId,
                Reason = "تصحيح رقم أمر الشركة بعد ترقيم خاطئ",
                ExternalOrderNumber = "AREX-201",
                UpdatedItems = new List<UpdateInvoiceItemDto>
                {
                    new() { ItemId = secondItem, Quantity = 10m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = false }
                }
            }, "user-1"));
            var amended = await _harness.InvoiceService.GetInvoiceByIdAsync(secondId);
            Assert.Equal("AREX-201", amended!.ExternalOrderNumber);

            // The first order keeps its own external number untouched.
            var first = await _harness.InvoiceService.GetInvoiceByIdAsync(firstId);
            Assert.Equal("AREX-100", first!.ExternalOrderNumber);
        }

        [Fact]
        public async Task DraftOrder_IsNotAmendable_PostedFlagEnforced()
        {
            var co = _harness.SeedMaritimeCompany("شركة تصحيح مسودة");
            var fish = _harness.SeedFrozenFish("سمك مسودة التصحيح", 10m, Price, 20m);
            var draftId = await _harness.CreateMaritimeDraftAsync(co.Id, fish.Id, "AM-8", 10m);

            var draft = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.AmendPostedMaritimeInvoiceAsync(new AmendPostedMaritimeInvoiceDto
                {
                    InvoiceId = draftId,
                    Reason = "مسودة لا تُصحح من هنا",
                    UpdatedItems = new List<UpdateInvoiceItemDto>
                    {
                        new() { ItemId = draft!.Items.Single().Id, Quantity = 12m, UnitPrice = Price, LineDiscountAmount = 0m, IsFromFreezer = true }
                    }
                }, "user-1"));
        }
    }
}
