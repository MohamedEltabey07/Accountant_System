using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using DAL.Entities;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Accountant_sys.Tests
{
    public class OpeningBalancePaymentTests : IDisposable
    {
        private readonly TestHarness _harness;

        public OpeningBalancePaymentTests()
        {
            _harness = new TestHarness();
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public async Task CustomerOpeningBalance_AppearsInOpenInvoicesList()
        {
            var customer = _harness.SeedWholesaleCustomer("العروسه");
            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                CustomerId = customer.Id,
                Amount = 6000m,
                EffectiveDate = DateTime.Today.AddDays(-10)
            });
            await _harness.Context.SaveChangesAsync();

            var openItems = await _harness.PaymentService.GetCustomerOpenInvoicesAsync(customer.Id);

            Assert.Single(openItems);
            var openingRow = openItems.First();
            Assert.Equal(0, openingRow.InvoiceId);
            Assert.Equal("رصيد افتتاحي سابق", openingRow.InvoiceNumber);
            Assert.Equal(6000m, openingRow.TotalAmount);
            Assert.Equal(0m, openingRow.PaidAmount);
            Assert.Equal(6000m, openingRow.RemainingBalance);
            Assert.Equal(InvoicePaymentStatus.Unpaid, openingRow.PaymentStatus);
        }

        [Fact]
        public async Task CustomerPayment_CanSettleOpeningBalancePartiallyAndFully()
        {
            var customer = _harness.SeedWholesaleCustomer("العروسه");
            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                CustomerId = customer.Id,
                Amount = 6000m,
                EffectiveDate = DateTime.Today.AddDays(-10)
            });
            await _harness.Context.SaveChangesAsync();

            // 1. Partial payment of 2000 EGP
            var tx1 = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                Amount = 2000m,
                PaymentDate = DateTime.Today,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = 0, AllocatedAmount = 2000m }
                }
            });

            Assert.True(tx1 > 0);
            // No row in PaymentAllocation because it's for opening balance (no foreign key to Invoice)
            Assert.Empty(_harness.Context.PaymentAllocations.Where(pa => pa.CashTransactionId == tx1));

            // Customer balance drops to 4000
            var balanceAfterFirst = await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
            Assert.Equal(4000m, balanceAfterFirst);

            // Open items should now reflect 4000 remaining and 2000 paid
            var openAfterFirst = await _harness.PaymentService.GetCustomerOpenInvoicesAsync(customer.Id);
            Assert.Single(openAfterFirst);
            Assert.Equal(4000m, openAfterFirst[0].RemainingBalance);
            Assert.Equal(2000m, openAfterFirst[0].PaidAmount);
            Assert.Equal(InvoicePaymentStatus.PartiallyPaid, openAfterFirst[0].PaymentStatus);

            // 2. Paying more than remaining 4000 should throw
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
                {
                    CustomerId = customer.Id,
                    Amount = 5000m,
                    PaymentDate = DateTime.Today,
                    Allocations = new List<InvoiceAllocationItemDto>
                    {
                        new() { InvoiceId = 0, AllocatedAmount = 5000m }
                    }
                }));

            // 3. Settle remaining 4000 in full
            var tx2 = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                Amount = 4000m,
                PaymentDate = DateTime.Today,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = 0, AllocatedAmount = 4000m }
                }
            });

            Assert.True(tx2 > 0);
            var balanceFinal = await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
            Assert.Equal(0m, balanceFinal);

            // Open items should now be empty (fully settled)
            var openFinal = await _harness.PaymentService.GetCustomerOpenInvoicesAsync(customer.Id);
            Assert.Empty(openFinal);
        }

        [Fact]
        public async Task MixedPayment_SettlesOpeningBalanceAndInvoiceCorrectly()
        {
            var customer = _harness.SeedWholesaleCustomer("العروسه");
            var fish = _harness.SeedFrozenFish("سمك بلطي", 10m);

            // Opening balance 1000
            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                CustomerId = customer.Id,
                Amount = 1000m,
                EffectiveDate = DateTime.Today.AddDays(-10)
            });

            // Invoice 3000
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
                UnitPrice = 30m
            });
            await _harness.InvoiceService.PostInvoiceAsync(draftId);

            var totalBalanceBefore = await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
            Assert.Equal(4000m, totalBalanceBefore);

            var openItems = await _harness.PaymentService.GetCustomerOpenInvoicesAsync(customer.Id);
            Assert.Equal(2, openItems.Count);
            Assert.Equal(0, openItems[0].InvoiceId); // Opening balance first
            Assert.Equal(draftId, openItems[1].InvoiceId);

            // Pay 2500 total: 1000 settles opening balance, 1500 settles invoice
            var tx = await _harness.PaymentService.RecordCustomerPaymentAsync(new CreateCustomerPaymentDto
            {
                CustomerId = customer.Id,
                Amount = 2500m,
                PaymentDate = DateTime.Today,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = 0, AllocatedAmount = 1000m },
                    new() { InvoiceId = draftId, AllocatedAmount = 1500m }
                }
            });

            Assert.True(tx > 0);

            // Only the invoice allocation was saved to PaymentAllocations
            var savedAllocations = _harness.Context.PaymentAllocations.Where(pa => pa.CashTransactionId == tx).ToList();
            Assert.Single(savedAllocations);
            Assert.Equal(draftId, savedAllocations[0].InvoiceId);
            Assert.Equal(1500m, savedAllocations[0].Amount);

            // Balance drops to 1500
            var balanceAfter = await _harness.UnitOfWork.CustomerRepo.GetCustomerBalanceAsync(customer.Id);
            Assert.Equal(1500m, balanceAfter);

            // Invoice remaining is 1500
            var invoiceRemaining = await _harness.InvoiceService.GetInvoiceRemainingBalanceAsync(draftId);
            Assert.Equal(1500m, invoiceRemaining);

            // Open items should now contain only the invoice with 1500 remaining
            var openAfter = await _harness.PaymentService.GetCustomerOpenInvoicesAsync(customer.Id);
            Assert.Single(openAfter);
            Assert.Equal(draftId, openAfter[0].InvoiceId);
            Assert.Equal(1500m, openAfter[0].RemainingBalance);
        }

        [Fact]
        public async Task VendorOpeningBalance_CanBeSettled()
        {
            var vendor = new Vendor { Name = "مورد أسماك", IsActive = true, PhoneNumber = "01000000000" };
            _harness.Context.Vendors.Add(vendor);
            await _harness.Context.SaveChangesAsync();

            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                VendorId = vendor.Id,
                Amount = 5000m,
                EffectiveDate = DateTime.Today.AddDays(-5)
            });
            await _harness.Context.SaveChangesAsync();

            var openItems = await _harness.PaymentService.GetVendorOpenInvoicesAsync(vendor.Id);
            Assert.Single(openItems);
            Assert.Equal(0, openItems[0].InvoiceId);
            Assert.Equal(5000m, openItems[0].RemainingBalance);

            // Pay 2000
            var tx = await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                Amount = 2000m,
                PaymentDate = DateTime.Today,
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = 0, AllocatedAmount = 2000m }
                }
            });

            Assert.True(tx > 0);
            var balance = await _harness.UnitOfWork.Vendors.GetVendorBalanceAsync(vendor.Id);
            Assert.Equal(3000m, balance);

            var openAfter = await _harness.PaymentService.GetVendorOpenInvoicesAsync(vendor.Id);
            Assert.Single(openAfter);
            Assert.Equal(3000m, openAfter[0].RemainingBalance);
            Assert.Equal(2000m, openAfter[0].PaidAmount);
        }
    }
}
