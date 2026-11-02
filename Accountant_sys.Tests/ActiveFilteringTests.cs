using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Cash.InComingData;
using BLL.DTOs.OpeningBalance.InComingData;
using BLL.IService;
using BLL.Service;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Active-only rule for operational selections, with historical integrity:
    // deactivating a record removes it from new-operation selectors and index
    // listings while old invoices, balances and stock history remain intact.
    public class ActiveFilteringTests : IDisposable
    {
        private readonly TestHarness _harness;
        private readonly CashTransactionService _cashTransactionService;
        private readonly OpeningBalanceService _openingBalanceService;

        public ActiveFilteringTests()
        {
            _harness = new TestHarness();
            _cashTransactionService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            _openingBalanceService = new OpeningBalanceService(_harness.UnitOfWork, _harness.Mapper);
        }

        // ===== Product selector =====

        [Fact]
        public async Task InactiveProduct_ExcludedFromSelectors_AndStatusViewsFilterable()
        {
            var fish = _harness.SeedFrozenFish("سمك تُوقف", 10m, 30m);
            var rice = _harness.SeedRegularProduct("رز متاح");

            await _harness.ProductService.ToggleStatusAsync(fish.Id);

            var positveSelectors = await _harness.ProductService.GetActiveProductsAsync();
            Assert.Contains(positveSelectors!, p => p.Id == rice.Id);
            Assert.DoesNotContain(positveSelectors!, p => p.Id == fish.Id);

            var statusActive = await _harness.ProductService.GetProductsByActiveStatusAsync(true);
            Assert.DoesNotContain(statusActive, p => p.Id == fish.Id);

            var statusInactive = await _harness.ProductService.GetProductsByActiveStatusAsync(false);
            Assert.Single(statusInactive, p => p.Id == fish.Id);

            var statusAll = await _harness.ProductService.GetProductsByActiveStatusAsync(null);
            Assert.Equal(2, statusAll.Count);
        }

        // ===== Customer / vendor / maritime selectors =====

        [Fact]
        public async Task InactiveCustomer_ExcludedFromNewOperationSelectors()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل معطل");

            await _harness.CustomerService.ToggleStatusAsync(customer.Id);

            var activeSelectors = await _harness.CustomerService.GetActiveCustomersAsync();
            Assert.DoesNotContain(activeSelectors!, c => c.Id == customer.Id);

            var statusInactive = await _harness.CustomerService.GetCustomersByActiveStatusAsync(false);
            Assert.Single(statusInactive!, c => c.Id == customer.Id);
        }

        [Fact]
        public async Task InactiveVendor_ExcludedFromNewOperationSelectors()
        {
            var vendorName = "مورد معطل";
            var vendor = await _harness.VendorService.CreateVendorAsync(new BLL.DTOs.Vendor.InComingData.CreateVendorDto { Name = vendorName });
            var vendorId = (await _harness.VendorService.GetActiveVendorAsync())!.Single(v => v.Name == vendorName).Id;

            await _harness.VendorService.ToggleStatusAsync(vendorId);

            var activeVendors = await _harness.VendorService.GetActiveVendorAsync();
            Assert.DoesNotContain(activeVendors!, v => v.Id == vendorId);

            var statusInactive = await _harness.VendorService.GetVendorsByActiveStatusAsync(false);
            Assert.Single(statusInactive!, v => v.Id == vendorId);
        }

        [Fact]
        public async Task InactiveMaritimeCompany_ExcludedFromNewSupplyOrderSelector()
        {
            var company = _harness.SeedMaritimeCompany("شركة معطلة");

            await _harness.MaritimeService.ToggleStatusAsync(company.Id);

            var activeCompanies = await _harness.MaritimeService.GetActiveCompaniesAsync();
            Assert.DoesNotContain(activeCompanies!, c => c.Id == company.Id);

            var statusInactive = await _harness.MaritimeService.GetCompaniesByActiveStatusAsync(false);
            Assert.Single(statusInactive, c => c.Id == company.Id);
        }

        // ===== Expense category selector + server-side guard =====

        [Fact]
        public async Task InactiveExpenseCategory_Excluded_AndExpenseRejected()
        {
            var category = new DAL.Entities.ExpenseCategory { Name = "إيجار معطل", IsActive = true };
            await _harness.UnitOfWork.ExpenseCategoryRepo.AddAsync(category);
            await _harness.UnitOfWork.SaveChangesAsync();

            await _harness.ExpenseCategoryService.ToggleCategoryStatusAsync(category.Id);

            var activeCategories = await _harness.ExpenseCategoryService.GetActiveCategoriesAsync();
            Assert.DoesNotContain(activeCategories, c => c.Id == category.Id);

            var inactiveExpense = new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 100m,
                ExpenseCategoryId = category.Id,
                Notes = "مصروف على بند معطل"
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => _cashTransactionService.RecordExpenseAsync(inactiveExpense));
        }

        // ===== Opening balance guard =====

        [Fact]
        public async Task OpeningBalance_ForInactiveCustomer_Rejected()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل تعطيل فتحول");
            await _harness.CustomerService.ToggleStatusAsync(customer.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _openingBalanceService.SetCustomerOpeningBalanceAsync(new CreateOpeningBalanceDto
                {
                    CustomerId = customer.Id,
                    Amount = 500m
                }));
        }

        // ===== Historical integrity =====

        [Fact]
        public async Task HistoricalInvoice_WithInactiveProduct_KeepsName_Balance_AndStock()
        {
            var company = _harness.SeedMaritimeCompany("شركة تاريخية");
            var fish = _harness.SeedFrozenFish("سمك تاريخي", 10m, 25m);
            await _harness.CreateStockInAsync(fish.Id, 10m); // 100 kg

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new BLL.DTOs.Invoice.InComingData.CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = DateTime.Today,
                CustomerId = company.Id,
                ExternalOrderNumber = "HIST-01",
                SupplyOrderDate = DateTime.Today
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new BLL.DTOs.Invoice.InComingData.AddInvoiceItemDto
            {
                InvoiceId = draftId,
                ProductId = fish.Id,
                Quantity = 20m,
                UnitPrice = 25m,
                IsFromFreezer = true
            });
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));

            // Deactivate the product AFTER the historical sale.
            await _harness.ProductService.ToggleStatusAsync(fish.Id);

            // The old invoice still shows the historical product name and price.
            var invoice = await _harness.InvoiceService.GetInvoiceByIdAsync(draftId);
            Assert.Equal("سمك تاريخي", invoice!.Items.Single().ProductName);
            Assert.Equal(25m, invoice.Items.Single().UnitPrice);

            // Balances still include the historical transaction.
            Assert.Equal(20m * 25m, await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(company.Id));

            // Stock balance still counts movements of the deactivated product.
            var balance = await _harness.StockService.GetBalanceAsync(fish.Id);
            Assert.Equal(80m, balance!.AvailableKg);
            Assert.False(balance.IsActive); // flagged inactive, but the balance remains
        }

        // ===== Vendor purchase guard =====

        [Fact]
        public async Task PurchaseDraft_WithInactiveVendor_Rejected()
        {
            var vendorName = "مورد للتعطيل";
            await _harness.VendorService.CreateVendorAsync(new BLL.DTOs.Vendor.InComingData.CreateVendorDto { Name = vendorName });
            var vendorId = (await _harness.VendorService.GetActiveVendorAsync())!.Single(v => v.Name == vendorName).Id;
            await _harness.VendorService.ToggleStatusAsync(vendorId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.InvoiceService.CreateDraftInvoiceAsync(new BLL.DTOs.Invoice.InComingData.CreateInvoiceDraftDto
                {
                    Type = InvoiceType.VendorPurchase,
                    TransactionDate = DateTime.Today,
                    VendorId = vendorId
                }));
        }

        public void Dispose() => _harness.Dispose();
    }
}
