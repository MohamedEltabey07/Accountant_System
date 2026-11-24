using Accountant_sys.Models;
using Accountant_sys.Tests.Helpers;
using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using BLL.Service;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    public class StatementRedesignTests : IDisposable
    {
        private readonly TestHarness _harness;
        private readonly CashTransactionService _cashService;
        private readonly OpeningBalanceService _openingService;

        public StatementRedesignTests()
        {
            _harness = new TestHarness();
            _cashService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            _openingService = new OpeningBalanceService(_harness.UnitOfWork, _harness.Mapper);
        }

        private Vendor SeedVendor(string name)
        {
            var vendor = new Vendor { Name = name, IsActive = true, PhoneNumber = "01012345678", Address = "السويس" };
            _harness.UnitOfWork.Vendors.AddAsync(vendor).Wait();
            _harness.UnitOfWork.SaveChangesAsync().Wait();
            return vendor;
        }

        private async Task<int> PostVendorPurchaseAsync(int vendorId, DateTime date, params (int ProductId, decimal Qty, decimal Price, decimal Discount)[] items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.VendorPurchase,
                TransactionDate = date,
                VendorId = vendorId
            });

            foreach (var (productId, qty, price, discount) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price,
                    LineDiscountAmount = discount
                });
            }

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return draftId;
        }

        private async Task<int> PostWholesaleSaleAsync(int customerId, DateTime date, decimal invoiceDiscount, params (int ProductId, decimal Qty, decimal Price)[] items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = date,
                CustomerId = customerId,
                InvoiceDiscountAmount = invoiceDiscount
            });

            foreach (var (productId, qty, price) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price
                });
            }

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return draftId;
        }

        [Fact]
        public async Task ScenarioA_ShortVendorStatement_ReconcilesCleanly()
        {
            var vendor = SeedVendor("الركن الازرق");
            var calamari = _harness.SeedRegularProduct("كاليماري");
            var fillet = _harness.SeedRegularProduct("سمك فيليه احمر");

            var invId = await PostVendorPurchaseAsync(vendor.Id, new DateTime(2026, 11, 17),
                (calamari.Id, 300m, 140m, 0m),
                (fillet.Id, 500m, 105m, 0m));

            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = new DateTime(2026, 11, 17),
                Amount = 50000m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = invId, AllocatedAmount = 50000m }
                }
            });

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Vendor, vendor.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            var model = StatementBuilder.Build(
                ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor",
                new DateTime(2026, 11, 1), new DateTime(2026, 12, 1),
                StatementAccountType.Vendor, vendor.PhoneNumber, vendor.Address);

            Assert.Equal(StatementAccountType.Vendor, model.AccountType);
            Assert.Equal($"VEN-{vendor.Id:D4}", model.AccountCode);
            Assert.Equal(0m, model.BeginningBalance);
            Assert.Equal(94500m, model.TotalInvoices);
            Assert.Equal(50000m, model.TotalPayments);
            Assert.Equal(44500m, model.EndingBalance);
            Assert.Equal(2, model.Lines.Count);
            Assert.Equal(44500m, await _harness.VendorService.GetVendorBalanceAsync(vendor.Id));
        }

        [Fact]
        public async Task ScenarioB_LargeVendorStatement_Matches26OperationsAndChronologicalOrder()
        {
            var vendor = SeedVendor("علي المصري");
            var mackerel = _harness.SeedRegularProduct("ماكريل");
            var shrimp = _harness.SeedRegularProduct("جمبري مقشر");
            var salmon = _harness.SeedRegularProduct("سلمون");
            var fillet = _harness.SeedRegularProduct("سمك فيليه ابيض");
            var calamari = _harness.SeedRegularProduct("كاليماري");
            var morgan = _harness.SeedRegularProduct("مرجان احمر");
            var crabs = _harness.SeedRegularProduct("كابوريا مجمده");
            var bouri = _harness.SeedRegularProduct("بوري");

            // 1. Purchase on 10/11/2026 (35,000) — appears BEFORE opening balance on 11/11/2026
            var pur24Id = await PostVendorPurchaseAsync(vendor.Id, new DateTime(2026, 11, 10), (mackerel.Id, 500m, 70m, 0m));

            // 2. Opening balance dated 11/11/2026 (99,700)
            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                VendorId = vendor.Id,
                Amount = 99700m,
                EffectiveDate = new DateTime(2026, 11, 11),
                CreatedAt = DateTime.UtcNow
            });
            await _harness.Context.SaveChangesAsync();

            // Remaining 19 purchase invoices on 11/11/2026 totaling 136,366 (so 35,000 + 136,366 = 171,366)
            var d11 = new DateTime(2026, 11, 11);
            await PostVendorPurchaseAsync(vendor.Id, d11, (shrimp.Id, 4m, 280m, 0m));          // 1,120
            var pur04Id = await PostVendorPurchaseAsync(vendor.Id, d11, (mackerel.Id, 50m, 650m, 0m)); // 32,500
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 20.48m, 450m, 0m));      // 9,216
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 10.30m, 450m, 0m));      // 4,635
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 140m, 105m, 0m));        // 14,700
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 20m, 105m, 0m));         // 2,100
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 135m, 0m));       // 2,700
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 135m, 0m));       // 1,350
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 20m, 105m, 0m));         // 2,100
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 140m, 110m, 0m));        // 15,400
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 10m, 110m, 0m));         // 1,100
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 130m, 0m));       // 1,300
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 130m, 0m));       // 2,600
            await PostVendorPurchaseAsync(vendor.Id, d11, (morgan.Id, 50m, 150m, 0m));         // 7,500
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 130m, 0m));       // 1,300
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 21.84m, 500m, 0m));      // 10,920
            await PostVendorPurchaseAsync(vendor.Id, d11, (shrimp.Id, 10m, 400m, 0m));         // 4,000
            var pur20Id = await PostVendorPurchaseAsync(vendor.Id, d11, (crabs.Id, 2.5m, 130m, 0m), (shrimp.Id, 30m, 570m, 0m)); // 17,425
            var pur21Id = await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 130m, 0m), (bouri.Id, 20m, 90m, 0m));  // 4,400

            // Draft + Cancelled invoices must be excluded
            var draftOnly = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.VendorPurchase,
                TransactionDate = d11,
                VendorId = vendor.Id
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftOnly,
                ProductId = mackerel.Id,
                Quantity = 100m,
                UnitPrice = 100m
            });
            var cancelledId = await PostVendorPurchaseAsync(vendor.Id, d11, (mackerel.Id, 10m, 500m, 0m));
            await _harness.InvoiceService.CancelPostedInvoiceAsync(new CancelInvoiceDto
            {
                InvoiceId = cancelledId,
                CancellationReason = "اختبار"
            });

            // 5 payments totaling 175,200
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 63000m,
                Notes = "حساب مرجان خالص بيد علي المصري",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 63000m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 20800m,
                Notes = "دفعة حساب كاستيلا خالص",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 20800m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 32500m,
                Notes = "دفعة ماكريل خالص",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = pur04Id, AllocatedAmount = 32500m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 8900m,
                Notes = "المفروض يكون 9100 عشان الجمبري الي علي اخده 13x700 = 9100 و كان هو دافع 200 للسواق عشان بضاعه للمحل ف هخصمهم من ال 9100 ف هيبقوا 8900",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 8900m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = new DateTime(2026, 11, 17),
                Amount = 50000m,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = pur20Id, AllocatedAmount = 17425m },
                    new() { InvoiceId = pur21Id, AllocatedAmount = 4400m },
                    new() { InvoiceId = pur24Id, AllocatedAmount = 28175m }
                }
            });

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Vendor, vendor.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            var model = StatementBuilder.Build(
                ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor", null, null,
                StatementAccountType.Vendor, vendor.PhoneNumber, vendor.Address);

            var detailedInvoices = await _harness.InvoiceService.GetPostedInvoiceDetailsAsync(
                InvoiceType.VendorPurchase, null, vendor.Id, null, null);

            Assert.Equal(26, model.Lines.Count);
            Assert.Equal(20, detailedInvoices.Count);
            Assert.Equal(0m, model.BeginningBalance);
            Assert.Equal(99700m, model.TotalOpening);
            Assert.Equal(171366m, model.TotalInvoices);
            Assert.Equal(271066m, model.TotalAdditions);
            Assert.Equal(175200m, model.TotalPayments);
            Assert.Equal(95866m, model.EndingBalance);
            Assert.Equal(95866m, await _harness.VendorService.GetVendorBalanceAsync(vendor.Id));

            // Chronological order check: 10/11 purchase (35,000) -> 11/11 opening (99,700 -> 134,700)
            Assert.Equal(new DateTime(2026, 11, 10), model.Lines[0].Date.Date);
            Assert.Equal(35000m, model.Lines[0].RunningBalance);
            Assert.Equal(StatementLineKind.OpeningBalance, model.Lines[1].Kind);
            Assert.Equal(new DateTime(2026, 11, 11), model.Lines[1].Date.Date);
            Assert.Equal(134700m, model.Lines[1].RunningBalance);

            // Multi-invoice allocation payment check on last row (VCH)
            var lastLine = model.Lines[^1];
            Assert.NotNull(lastLine.CashTransaction);
            Assert.Equal(3, lastLine.CashTransaction!.Allocations.Count);
            Assert.Equal(50000m, lastLine.CashTransaction.Allocations.Sum(a => a.Amount));
            Assert.Equal("موزعة على 3 فواتير", lastLine.SummaryHint);

            // Presentation layer: unpaid section + reconciliation box must match
            // the acceptance numbers exactly, without re-computing ledger totals.
            var settlements = await _harness.InvoiceService.GetStatementInvoiceSettlementsAsync(
                InvoiceType.VendorPurchase, null, vendor.Id, null, null, null);
            StatementPresentationBuilder.Apply(model, settlements);

            Assert.Equal(99700m, model.OpeningDisplay);
            Assert.Equal(17, model.UnpaidInvoices.Count);
            Assert.Equal(88866m, model.UnpaidTotal);
            Assert.Equal(92700m, model.OnAccountTotal);
            Assert.Equal(0m, model.OverpaymentTotal);
            Assert.Equal(0m, model.CrossPeriodBridge);
            Assert.Equal(95866m, model.EndingBalance);
            Assert.Equal(50000m, model.LastPaymentAmount);
            Assert.Equal(new DateTime(2026, 11, 17), model.LastPaymentDate);

            var pur24 = model.UnpaidInvoices.Single(i => i.InvoiceId == pur24Id);
            Assert.Equal(35000m, pur24.TotalAmount);
            Assert.Equal(28175m, pur24.PaidAmount);
            Assert.Equal(6825m, pur24.RemainingAmount);
            Assert.Equal(DAL.Enums.InvoicePaymentStatus.PartiallyPaid, pur24.PaymentStatus);

            var pur03 = model.UnpaidInvoices.Single(i => i.TotalAmount == 1120m);
            Assert.Equal(0m, pur03.PaidAmount);
            Assert.Equal(1120m, pur03.RemainingAmount);
            Assert.Equal(DAL.Enums.InvoicePaymentStatus.Unpaid, pur03.PaymentStatus);
        }

        [Fact]
        public async Task OpeningBalance_BeforeVsWithinPeriod_NeverDoubleCounted()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل فترة");
            var product = _harness.SeedRegularProduct("سمك طازج");

            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                CustomerId = customer.Id,
                Amount = 5000m,
                EffectiveDate = new DateTime(2026, 10, 31)
            });
            await _harness.Context.SaveChangesAsync();

            var invId = await PostWholesaleSaleAsync(customer.Id, new DateTime(2026, 11, 5), 0m, (product.Id, 10m, 200m));
            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = new DateTime(2026, 11, 10),
                Amount = 1500m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = invId, AllocatedAmount = 1500m } }
            });

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Customer, customer.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            // Window starting 01/11/2026: opening balance on 31/10/2026 goes into BeginningBalance only
            var filtered = StatementBuilder.Build(
                ledger, isVendor: false, customer.Id, customer.Name, "Customer",
                new DateTime(2026, 11, 1), new DateTime(2026, 11, 30),
                StatementAccountType.Customer);

            Assert.Equal(5000m, filtered.BeginningBalance);
            Assert.Equal(0m, filtered.TotalOpening);
            Assert.Equal(2000m, filtered.TotalInvoices);
            Assert.Equal(1500m, filtered.TotalPayments);
            Assert.Equal(5500m, filtered.EndingBalance);
            Assert.Equal(2, filtered.Lines.Count);

            // Empty window (01/12 - 15/12): carry-forward equals ending balance, 0 movements
            var emptyWindow = StatementBuilder.Build(
                ledger, isVendor: false, customer.Id, customer.Name, "Customer",
                new DateTime(2026, 12, 1), new DateTime(2026, 12, 15),
                StatementAccountType.Customer);

            Assert.Equal(5500m, emptyWindow.BeginningBalance);
            Assert.Equal(5500m, emptyWindow.EndingBalance);
            Assert.Empty(emptyWindow.Lines);
        }

        [Fact]
        public async Task CreditBalances_And_ReturnsWithRefunds_PreserveSignConventions()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل مرتجع");
            var fish = _harness.SeedFrozenFish("سمك قاروص", 10m, 50m);
            var invId = await PostWholesaleSaleAsync(customer.Id, new DateTime(2026, 11, 2), 0m, (fish.Id, 10m, 100m)); // 1000

            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = new DateTime(2026, 11, 3),
                Amount = 1000m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = invId, AllocatedAmount = 1000m } }
            });

            // Return 300 without cash refund -> customer enters -300 credit balance
            var cnId = await _harness.CreditNoteService.CreateDraftCreditNoteAsync(new CreateCreditNoteDraftDto
            {
                OriginalInvoiceId = invId,
                TransactionDate = new DateTime(2026, 11, 4),
                Reason = "فرق وزن",
                Items = new List<CreateCreditNoteItemDto> { new() { ProductId = fish.Id, Quantity = 3m, UnitPrice = 100m } }
            });
            await _harness.CreditNoteService.PostCreditNoteAsync(new PostCreditNoteDto { CreditNoteId = cnId, IsCashRefund = false });

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Customer, customer.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);
            var model = StatementBuilder.Build(
                ledger, isVendor: false, customer.Id, customer.Name, "Customer", null, null, StatementAccountType.Customer);

            Assert.Equal(-300m, model.EndingBalance);
            Assert.Equal(300m, model.TotalReturns);
            Assert.Equal("(له)", StatementBuilder.FormatBalanceTag(model.EndingBalance, isVendor: false));
            Assert.Equal(-300m, await _harness.CustomerService.GetCustomerBalanceAsync(customer.Id));
        }

        [Fact]
        public async Task MaritimeStatement_ExternalOrderNumbers_AdditionalCharges_And_SummaryDetailedParity()
        {
            var company = _harness.SeedMaritimeCompany("شركة كونكورد البحرية");
            var fish = _harness.SeedFrozenFish("سمك سلمون", 10m, 100m);
            await _harness.CreateStockInAsync(fish.Id, 20m); // 200 kg

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = new DateTime(2026, 11, 5),
                SupplyOrderDate = new DateTime(2026, 11, 5),
                CustomerId = company.Id,
                ExternalOrderNumber = "EXT-48356"
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 50m, // 5000
                UnitPrice = 100m,
                IsFromFreezer = true
            });
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "ايس بوكس",
                Quantity = 5m,
                UnitPrice = 100m,
                IsBillableToCompany = true
            });
            await _harness.InvoiceService.AddChargeToDraftAsync(new AddInvoiceChargeDto
            {
                InvoiceId = draftId,
                Description = "نقل داخلي",
                Amount = 200m,
                IsBillableToCompany = false
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.MaritimeCompany, company.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);
            var model = StatementBuilder.Build(
                ledger, isVendor: false, company.Id, company.Name, "MaritimeCompany", null, null,
                StatementAccountType.MaritimeCompany, "01200000000", "بورسعيد");
            var orders = await _harness.InvoiceService.GetMaritimeSupplyOrdersAsync(company.Id, null, null);

            Assert.Equal(StatementAccountType.MaritimeCompany, model.AccountType);
            Assert.Equal($"MAR-{company.Id:D4}", model.AccountCode);
            Assert.True(model.ShowExternalOrderColumn);
            Assert.Single(model.Lines);
            Assert.Equal("EXT-48356", model.Lines[0].DocumentNo);
            Assert.StartsWith("MAR-", model.Lines[0].SecondaryDocNo);
            Assert.Equal(5500m, model.TotalInvoices); // 5000 + 500 billable charge (excludes 200 internal)
            Assert.Equal(5500m, orders.Single().TotalAmount);
            Assert.Equal(500m, orders.Single().BillableChargesTotal);
            Assert.Equal(200m, orders.Single().InternalChargesTotal);
            Assert.Equal(5500m, await _harness.MaritimeService.GetCompanyBalanceAsync(company.Id));
        }

        [Fact]
        public void FormattingAndArabicVocabulary_MeetSpecification()
        {
            Assert.Equal("500", StatementBuilder.FormatQuantity(500.000m));
            Assert.Equal("20.48", StatementBuilder.FormatQuantity(20.480m));
            Assert.Equal("2.5", StatementBuilder.FormatQuantity(2.500m));
            Assert.Equal("1,200", StatementBuilder.FormatQuantity(1200.000m));
            Assert.Equal("10/11/2026", StatementBuilder.FormatDate(new DateTime(2026, 11, 10)));

            var words = Tafqeet.ToArabicWords(95866m);
            Assert.Equal(1, words.Split("فقط", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, words.Split("لا غير", StringSplitOptions.None).Length - 1);

            var vendorLabels = StatementBuilder.GetLabels(StatementAccountType.Vendor);
            Assert.Equal("كشف حساب", vendorLabels.StatementTitle);
            Assert.Equal("فواتير الشراء", vendorLabels.InvoicesLabel);
            Assert.Equal("المدفوع", vendorLabels.PaymentsLabel);
            Assert.Equal("مرتجع مشتريات", vendorLabels.ReturnsLabel);
            Assert.Equal("المتبقي علينا", vendorLabels.OutstandingBalanceLabel);
            Assert.Equal("توقيع المورد", vendorLabels.PartySignatureLabel);

            var customerLabels = StatementBuilder.GetLabels(StatementAccountType.Customer);
            Assert.Equal("كشف حساب", customerLabels.StatementTitle);
            Assert.Equal("فواتير البيع", customerLabels.InvoicesLabel);
            Assert.Equal("المحصّل", customerLabels.PaymentsLabel);
            Assert.Equal("المتبقي على العميل", customerLabels.OutstandingBalanceLabel);

            var maritimeLabels = StatementBuilder.GetLabels(StatementAccountType.MaritimeCompany);
            Assert.Equal("كشف حساب", maritimeLabels.StatementTitle);
            Assert.Equal("أوامر التوريد", maritimeLabels.InvoicesLabel);
            Assert.Equal("المحصّل", maritimeLabels.PaymentsLabel);
            Assert.Equal("المتبقي على الشركة", maritimeLabels.OutstandingBalanceLabel);
        }

        [Fact]
        public async Task FilteredPeriod_UnpaidAndBridge_ReconcileExactly()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل تسوية");
            var product = _harness.SeedRegularProduct("جمبري");

            var oldInvoiceId = await PostWholesaleSaleAsync(
                customer.Id, new DateTime(2026, 11, 1), 0m, (product.Id, 10m, 100m)); // 1,000 before the period
            await PostWholesaleSaleAsync(
                customer.Id, new DateTime(2026, 11, 12), 0m, (product.Id, 2m, 100m)); // 200 inside the period

            await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                PaymentDate = new DateTime(2026, 11, 13),
                Amount = 400m,
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = oldInvoiceId, AllocatedAmount = 400m } }
            });

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Customer, customer.Id, _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            var from = new DateTime(2026, 11, 10);
            var to = new DateTime(2026, 11, 30);
            var model = StatementBuilder.Build(
                ledger, isVendor: false, customer.Id, customer.Name, "Customer", from, to,
                StatementAccountType.Customer);

            var settlements = await _harness.InvoiceService.GetStatementInvoiceSettlementsAsync(
                InvoiceType.WholesaleSales, customer.Id, null, from, to, to);
            StatementPresentationBuilder.Apply(model, settlements);

            Assert.Equal(1000m, model.BeginningBalance);
            Assert.Equal(1000m, model.OpeningDisplay);
            Assert.Equal(800m, model.EndingBalance);
            Assert.Single(model.UnpaidInvoices);
            Assert.Equal(200m, model.UnpaidTotal);
            Assert.Equal(0m, model.OnAccountTotal);
            Assert.Equal(-400m, model.CrossPeriodBridge);

            var bridged = model.UnpaidTotal + model.OpeningDisplay - model.OnAccountTotal
                          + model.TotalRefunds - model.OverpaymentTotal + model.CrossPeriodBridge;
            Assert.Equal(model.EndingBalance, bridged);
        }

        [Fact]
        public void CssEscaping_ProducesValidCssStrings()
        {
            var escaped = StatementCss.EscapeString("علي \"المصري\" <script>&\\\n");

            Assert.DoesNotContain("&#", escaped);
            Assert.DoesNotContain("<", escaped);
            Assert.DoesNotContain(">", escaped);
            Assert.Contains("\\\"", escaped);
            Assert.Contains("\\3C ", escaped);
            Assert.Contains("\\3E ", escaped);
            Assert.Contains("\\26 ", escaped);
            Assert.Contains("\\\\", escaped);
            Assert.Contains("\\A ", escaped);

            var pageHeader = StatementCss.BuildPageHeaderContent("كشف حساب", "علي <b>شركة</b>", "VEN-0008 & 1");
            Assert.DoesNotContain("<b>", pageHeader);
            Assert.DoesNotContain("&#", pageHeader);
            Assert.Contains("\\3C ", pageHeader);
            Assert.Contains("\\3E ", pageHeader);
            Assert.Contains("\\26 ", pageHeader);
        }

        [Fact]
        public void PublicNotes_SuppressLongAndInternalText()
        {
            Assert.Null(StatementNotes.GetPublicNote(new string('س', 121)));
            Assert.Equal("حساب مرجان خالص", StatementNotes.GetPublicNote("  حساب   مرجان خالص "));
            Assert.Null(StatementNotes.GetPublicNote(
                "المفروض يكون 9100 عشان الجمبري الي علي اخده و كان هو دافع 200 للسواق"));
        }

        [Fact]
        public void DetailLines_ShowAllocationsAndHideInternalNotes()
        {
            var labels = StatementBuilder.GetLabels(StatementAccountType.Vendor);
            var payment = new StatementLine
            {
                Kind = StatementLineKind.VendorPayment,
                Notes = "المفروض يكون 9100 عشان حساب قديم",
                CashTransaction = new BLL.DTOs.Cash.OutGoingData.CashTransactionDto
                {
                    Amount = 1000m,
                    Allocations = new List<BLL.DTOs.Payment.OutGoingData.PaymentAllocationDto>
                    {
                        new() { InvoiceId = 4, InvoiceNumber = "PUR-0004", Amount = 1000m }
                    }
                }
            };

            var lines = StatementPresentationBuilder.BuildDetailLines(payment, labels);

            Assert.Contains(lines, l => l.Text.Contains("PUR-0004") && l.Text.Contains("1,000.00"));
            Assert.DoesNotContain(lines, l => l.IsNote);
            Assert.Contains("pur-0004", StatementPresentationBuilder.BuildSearchText(payment));
        }

        [Fact]
        public void ProductDetailLines_IncludeStructuredFieldsAndRowTotals_AcrossAllAccountTypes()
        {
            var vendorLabels = StatementBuilder.GetLabels(StatementAccountType.Vendor);
            var maritimeLabels = StatementBuilder.GetLabels(StatementAccountType.MaritimeCompany);
            var customerLabels = StatementBuilder.GetLabels(StatementAccountType.Customer);

            // 1. Multi-item invoice (8 seafood items matching real statement scenario)
            var invoiceDetail = new BLL.DTOs.Invoice.OutGoingData.StatementInvoiceDetailDto
            {
                InvoiceId = 10,
                InvoiceNumber = "SAL-0010",
                Items = new List<BLL.DTOs.Invoice.OutGoingData.StatementInvoiceItemDto>
                {
                    new() { ProductName = "سمك فيليه ابيض", Quantity = 20m, UnitPrice = 140m, LineTotal = 2800m },
                    new() { ProductName = "سلمون كامل مجمد", Quantity = 12m, UnitPrice = 560m, LineTotal = 6720m },
                    new() { ProductName = "سمك فيليه احمر", Quantity = 20m, UnitPrice = 130m, LineTotal = 2600m },
                    new() { ProductName = "كابوريا مجمده", Quantity = 20m, UnitPrice = 140m, LineTotal = 2800m },
                    new() { ProductName = "جمبري مقشر", Quantity = 8m, UnitPrice = 850m, LineTotal = 6800m },
                    new() { ProductName = "ماكريل", Quantity = 15m, UnitPrice = 110m, LineTotal = 1650m },
                    new() { ProductName = "كاليماري حلقات", Quantity = 10m, UnitPrice = 230m, LineTotal = 2300m },
                    new() { ProductName = "كاليماري حلقات بالبقسماط", Quantity = 5m, UnitPrice = 220m, LineTotal = 1100m }
                }
            };

            var invoiceLines = StatementPresentationBuilder.BuildDetailLines(
                new StatementLine { Kind = StatementLineKind.Invoice, DocumentId = 10 },
                customerLabels,
                invoice: invoiceDetail);

            Assert.Equal(8, invoiceLines.Count);
            Assert.All(invoiceLines, l => Assert.NotNull(l.Product));
            Assert.Equal("2,800.00", invoiceLines[0].Product!.LineTotalText);
            Assert.Equal("6,720.00", invoiceLines[1].Product!.LineTotalText);
            Assert.Equal("2,600.00", invoiceLines[2].Product!.LineTotalText);
            Assert.Equal("2,800.00", invoiceLines[3].Product!.LineTotalText);
            Assert.Equal("6,800.00", invoiceLines[4].Product!.LineTotalText);
            Assert.Equal("1,650.00", invoiceLines[5].Product!.LineTotalText);
            Assert.Equal("2,300.00", invoiceLines[6].Product!.LineTotalText);
            Assert.Equal("1,100.00", invoiceLines[7].Product!.LineTotalText);

            // 2. Maritime supply order with line discount + billable charge
            var orderDetail = new BLL.DTOs.MaritimeCompany.OutGoingData.MaritimeSupplyOrderDto
            {
                InvoiceId = 20,
                Items = new List<BLL.DTOs.MaritimeCompany.OutGoingData.MaritimeSupplyOrderItemDto>
                {
                    new() { ProductName = "جمبري مقشر", Quantity = 10m, UnitPrice = 500m, LineDiscountAmount = 150m, LineTotal = 4850m }
                },
                Charges = new List<BLL.DTOs.Invoice.OutGoingData.InvoiceChargeDto>
                {
                    new() { Description = "ايس بوكس", Amount = 250m, IsBillableToCompany = true }
                }
            };

            var orderLines = StatementPresentationBuilder.BuildDetailLines(
                new StatementLine { Kind = StatementLineKind.MaritimeSupplyOrder, DocumentId = 20 },
                maritimeLabels,
                order: orderDetail);

            Assert.Equal(2, orderLines.Count);
            Assert.NotNull(orderLines[0].Product);
            Assert.Equal("10", orderLines[0].Product!.QuantityText);
            Assert.Equal("500.00", orderLines[0].Product!.UnitPriceText);
            Assert.Equal("150.00", orderLines[0].Product!.LineDiscountText);
            Assert.Equal("4,850.00", orderLines[0].Product!.LineTotalText);
            Assert.Null(orderLines[1].Product);
            Assert.Contains("ايس بوكس", orderLines[1].Text);

            // 3. Credit note / return items
            var returnLine = new StatementLine
            {
                Kind = StatementLineKind.CreditNoteReturn,
                CreditNote = new BLL.DTOs.CreditNote.OutGoingData.CreditNoteDto
                {
                    OriginalInvoiceNumber = "PUR-0005",
                    Items = new List<BLL.DTOs.CreditNote.OutGoingData.CreditNoteItemDto>
                    {
                        new() { ProductName = "سلمون", Quantity = 3m, UnitPrice = 450m, LineTotal = 1350m }
                    }
                }
            };

            var returnDetails = StatementPresentationBuilder.BuildDetailLines(returnLine, vendorLabels);
            Assert.Equal(2, returnDetails.Count);
            Assert.Null(returnDetails[0].Product);
            Assert.Contains("PUR-0005", returnDetails[0].Text);
            Assert.NotNull(returnDetails[1].Product);
            Assert.Equal("1,350.00", returnDetails[1].Product!.LineTotalText);
        }

        public void Dispose() => _harness.Dispose();
    }
}
