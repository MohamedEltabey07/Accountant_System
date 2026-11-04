using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Safe handling of erroneous maritime supply invoices: drafts are physically
    // deleted, posted orders are voided with stock reversal, allocations are
    // detached while cash stays, and external order numbers become reusable.
    public class MaritimeDeletionTests : IDisposable
    {
        private readonly TestHarness _harness;
        private const decimal Price = 40m;

        public MaritimeDeletionTests()
        {
            _harness = new TestHarness();
        }

        public void Dispose() => _harness.Dispose();

        private async Task<int> CreateDraftAsync(int companyId, int fishId, string orderNo, decimal kg, bool isFromFreezer = true)
        {
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
                ProductId = fishId,
                Quantity = kg,
                UnitPrice = isFromFreezer ? null : Price,
                IsFromFreezer = isFromFreezer
            });
            return draftId;
        }

        [Fact]
        public async Task DraftDelete_RemovesOrderAndItsChildren_Physically()
        {
            var co = _harness.SeedMaritimeCompany("شركة حذف مسودة");
            var fish = _harness.SeedFrozenFish("سمك الحذف", 10m, Price, 20m);
            var fishId2 = _harness.SeedFrozenFish("سمك الحذف 2", 12m, Price, 20m);
            var draftId = await CreateDraftAsync(co.Id, fish.Id, "DEL-1", 10m);
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId, ProductId = fishId2.Id, Quantity = 6m, IsFromFreezer = false, UnitPrice = Price
            });
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId, Description = "ثلج مسودة", Amount = 25m
            });

            Assert.True(await _harness.InvoiceService.DeleteDraftInvoiceAsync(draftId));

            // The draft and all of its draft-only children are gone: no orphans.
            Assert.Null(await _harness.InvoiceService.GetInvoiceByIdAsync(draftId));
            Assert.Empty(_harness.Context.InvoiceItems.Where(i => i.InvoiceId == draftId));
            Assert.Empty(_harness.Context.Set<InvoiceAdditionalCharge>().Where(c => c.InvoiceId == draftId));

            // A draft delete never touched the freezer ledger.
            Assert.Empty(_harness.Context.StockMovements.Where(m => m.SourceInvoiceId == draftId));
        }

        [Fact]
        public async Task DraftDelete_UnknownOrPosted_ReturnsFalse()
        {
            var co = _harness.SeedMaritimeCompany("شركة حذف مرحل");
            var fish = _harness.SeedFrozenFish("سمك حذف مرحل", 10m, Price, 20m);
            await _harness.CreateStockInAsync(fish.Id, 10m);

            Assert.False(await _harness.InvoiceService.DeleteDraftInvoiceAsync(9999));

            var draftId = await CreateDraftAsync(co.Id, fish.Id, "DEL-2", 10m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.False(await _harness.InvoiceService.DeleteDraftInvoiceAsync(draftId)); // posted: use void instead

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(InvoiceStatus.Posted, invoice!.Status); // untouched by the delete attempt
        }

        [Fact]
        public async Task VoidPostedOrder_ReversesFreezerStock_ExactlyOnce()
        {
            var co = _harness.SeedMaritimeCompany("شركة إلغاء مخزون");
            var fish = _harness.SeedFrozenFish("سمك الإلغاء", 10m, Price, 20m);
            await _harness.CreateStockInAsync(fish.Id, 15m); // 150 kg in

            var draftId = await CreateDraftAsync(co.Id, fish.Id, "DEL-3", 100m);
            await _harness.InvoiceService.PostInvoiceAsync(draftId);
            Assert.Equal(50m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            Assert.True(await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = draftId,
                CancellationReason = "أمر توريد مرسل بالخطأ لشركة أخرى"
            }));

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(InvoiceStatus.Cancelled, invoice!.Status);

            // Original Out movement kept + one reversal added (never edits the ledger lines).
            var movements = _harness.Context.StockMovements
                .Where(m => m.ProductId == fish.Id && m.SourceInvoiceId == draftId).ToList();
            Assert.Equal(2, movements.Count);
            Assert.Single(movements, m => m.MovementType == StockMovementType.In);

            Assert.Equal(150m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);

            // Voiding twice is guarded: already-cancelled orders are simply no-ops,
            // and the ledger must not be double-reversed.
            Assert.False(await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = draftId,
                CancellationReason = "إعادة إلغاء غير مطلوبة للتحقق من الحماية"
            }));
            Assert.Equal(2, _harness.Context.StockMovements.Count(m => m.ProductId == fish.Id && m.SourceInvoiceId == draftId));
            Assert.Equal(150m, (await _harness.StockService.GetBalanceAsync(fish.Id))!.AvailableKg);
        }

        [Fact]
        public async Task VoidPostedOrder_AllocationsDetached_CashPreserved_AsCustomerCredit()
        {
            var co = _harness.SeedMaritimeCompany("شركة إلغاء سداد");
            var fish = _harness.SeedFrozenFish("سمك إلغاء السداد", 10m, Price, 20m);

            var draftId = await CreateDraftAsync(co.Id, fish.Id, "DEL-4", 50m, isFromFreezer: false); // 2,000
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var cashTxId = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = co.Id,
                PaymentDate = DateTime.Today,
                Amount = 1200m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = draftId, AllocatedAmount = 1200m }
                }
            });
            Assert.True(cashTxId > 0);

            Assert.True(await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = draftId,
                CancellationReason = "أمر توريد مشكوك فيه، سند الدفع يبقى في الخزنة"
            }));

            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal(InvoiceStatus.Cancelled, invoice!.Status);

            // Allocation rows gone, the real cash receipt stays.
            Assert.Empty(_harness.Context.PaymentAllocations.Where(pa => pa.InvoiceId == draftId));
            var tx = _harness.Context.CashTransactions.Single(t => t.Id == cashTxId);
            Assert.Equal(CashDirection.In, tx.Direction);
            Assert.Equal(1200m, tx.Amount);

            // The receipt is now an unallocated credit: paid money exceeds owed sales.
            Assert.Equal(-1200m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(co.Id));

            // Maritime-specific allowance: allocation-holding orders CAN be voided
            // (unlike wholesale sales, which are still blocked).
        }

        [Fact]
        public async Task WholesaleSale_WithAllocation_StillBlocked() // scope: previous strict behaviour preserved
        {
            var customer = _harness.SeedWholesaleCustomer("عميل جملة إلغاء");
            var fish = _harness.SeedRegularProduct("رز جملة");
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customer.Id
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId, ProductId = fish.Id, Quantity = 10m, UnitPrice = 60m
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = DateTime.Today,
                Amount = 100m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = draftId, AllocatedAmount = 100m } }
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.InvoiceService.CancelPostedInvoiceAsync(
                new CancelInvoiceDto { InvoiceId = draftId, CancellationReason = "محاولة إلغاء فاتورة مسددة" }));
        }

        [Fact]
        public async Task CustomerBalance_SalesExcludeVoidedOrders()
        {
            var co = _harness.SeedMaritimeCompany("شركة رصيد مُلغى");
            var fish = _harness.SeedFrozenFish("سمك رصيد مُلغى", 10m, Price, 20m);

            var firstId = await CreateDraftAsync(co.Id, fish.Id, "DEL-5", 50m, isFromFreezer: false);  // 2,000
            var secondId = await CreateDraftAsync(co.Id, fish.Id, "DEL-6", 30m, isFromFreezer: false); // 1,200
            await _harness.InvoiceService.PostInvoiceAsync(firstId);
            await _harness.InvoiceService.PostInvoiceAsync(secondId);

            Assert.Equal(3200m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(co.Id));

            await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = firstId,
                CancellationReason = "أمر مكرر أُرسل بالخطأ"
            });

            // Sales sum only counts Posted orders: 2,000 (cancelled) drops out.
            Assert.Equal(1200m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(co.Id));

            var cancelled = await _harness.InvoiceService.GetInvoiceByIdAsync(firstId);
            Assert.Equal(InvoiceStatus.Cancelled, cancelled!.Status);
        }

        // ---------- External order number reuse/uniqueness ----------

        [Fact]
        public async Task DuplicateExternalNumber_SameCompany_ActiveOrders_Rejected()
        {
            var co = _harness.SeedMaritimeCompany("شركة تكرار رقم");
            var fish = _harness.SeedFrozenFish("سمك تكرار رقم", 10m, Price, 20m);
            await CreateDraftAsync(co.Id, fish.Id, "EXT-DUP", 10m, isFromFreezer: false);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateDraftAsync(co.Id, fish.Id, "EXT-DUP", 5m, isFromFreezer: false));
        }

        [Fact]
        public async Task SameExternalNumber_DifferentCompanies_Accepted()
        {
            var coA = _harness.SeedMaritimeCompany("شركة رقم مشترك أ");
            var coB = _harness.SeedMaritimeCompany("شركة رقم مشترك ب");
            var fish = _harness.SeedFrozenFish("سمك رقم مشترك", 10m, Price, 20m);

            await CreateDraftAsync(coA.Id, fish.Id, "EXT-SHARE", 10m, isFromFreezer: false);
            var secondDraft = await CreateDraftAsync(coB.Id, fish.Id, "EXT-SHARE", 7m, isFromFreezer: false);

            Assert.True(secondDraft > 0);
        }

        [Fact]
        public async Task ExternalNumber_ReusableAfterVoiding_SameCompany_WithInternalNumber()
        {
            var co = _harness.SeedMaritimeCompany("شركة إعادة رقم");
            var fish = _harness.SeedFrozenFish("سمك إعادة رقم", 10m, Price, 20m);
            await _harness.CreateStockInAsync(fish.Id, 10m);

            var firstId = await CreateDraftAsync(co.Id, fish.Id, "EXT-REUSE", 50m);
            await _harness.InvoiceService.PostInvoiceAsync(firstId);
            var firstInvoice = await _harness.InvoiceService.GetInvoiceByIdAsync(firstId);
            var firstInternalNumber = firstInvoice!.InvoiceNumber; // MAR-...

            await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = firstId,
                CancellationReason = "أمر توريد مرسل للشركة الخطأ"
            });

            // The very same external number is now free for the same company.
            var secondId = await CreateDraftAsync(co.Id, fish.Id, "EXT-REUSE", 30m);
            await _harness.InvoiceService.PostInvoiceAsync(secondId);
            var secondInvoice = await _harness.InvoiceService.GetInvoiceByIdAsync(secondId);

            Assert.Equal("EXT-REUSE", secondInvoice!.ExternalOrderNumber);
            Assert.NotEqual(firstInternalNumber, secondInvoice.InvoiceNumber); // fresh internal MAR-#### document
            Assert.StartsWith("MAR-", secondInvoice.InvoiceNumber);
            Assert.Equal("EXT-REUSE", firstInvoice.ExternalOrderNumber); // historical order keeps its label
        }
    }
}
