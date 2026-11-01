using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Cash.InComingData;
using BLL.DTOs.DailyClosing.InComingData;
using BLL.Service;
using DAL.Entities;
using DAL.Enums;
using Xunit;

namespace Accountant_sys.Tests
{
    // Cash drawer invariance: ExpectedClosingCashAmount must always equal
    // OpeningCashAmount + TotalCashIn - TotalCashOut for the business day,
    // and DifferenceAmount must equal Counted - Expected.
    public class CashDrawerTests : IDisposable
    {
        private readonly TestHarness _harness;
        private readonly CashTransactionService _cashService;
        private readonly DailyClosingService _closingService;

        public CashDrawerTests()
        {
            _harness = new TestHarness();
            _cashService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            _closingService = new DailyClosingService(_harness.UnitOfWork, _harness.Mapper);
        }

        [Fact]
        public async Task PreviewClosing_ExpectedEqualsInMinusOut_WithZeroOpening()
        {
            // A mixed business day of movements, all dated today.
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 700m,
                Category = CashCategory.RetailSale,
                Notes = "مبيعات كاش"
            });
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 300m,
                Category = CashCategory.OtherCashIn,
                Notes = "إيداع نقدي"
            });
            var expenseCategory = new ExpenseCategory { Name = "نثريات" };
            await _harness.UnitOfWork.ExpenseCategoryRepo.AddAsync(expenseCategory);
            await _harness.UnitOfWork.SaveChangesAsync();
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 250m,
                Category = CashCategory.Expense,
                ExpenseCategoryId = expenseCategory.Id,
                Notes = "مصروف نثري"
            });
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 150m,
                Category = CashCategory.OtherCashOut,
                Notes = "سحب نقدي"
            });

            // Movements dated outside the day must not leak into the totals.
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today.AddDays(-2),
                Amount = 9999m,
                Category = CashCategory.OtherCashIn,
                Notes = "يوم سابق"
            });

            var preview = await _closingService.CalculatePreviewClosingAsync(DateTime.Today);

            var inTx = _harness.Context.CashTransactions
                .Count(t => t.TransactionDate == DateTime.Today && t.Direction == CashDirection.In);
            var outTx = _harness.Context.CashTransactions
                .Count(t => t.TransactionDate == DateTime.Today && t.Direction == CashDirection.Out);
            Assert.Equal(2, inTx);
            Assert.Equal(2, outTx);

            Assert.Equal(1000m, preview.TotalCashIn);   // 700 + 300
            Assert.Equal(400m, preview.TotalCashOut);   // 250 + 150
            Assert.Equal(600m, preview.ExpectedClosingCashAmount); // opening 0 + 1000 - 400
        }

        [Fact]
        public async Task PerformDailyClosing_PersistsExactExpectedAndDifference()
        {
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 500m,
                Category = CashCategory.RetailSale
            });
            var expenseCategory = new ExpenseCategory { Name = "مواصلات" };
            await _harness.UnitOfWork.ExpenseCategoryRepo.AddAsync(expenseCategory);
            await _harness.UnitOfWork.SaveChangesAsync();
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Amount = 200m,
                Category = CashCategory.Expense,
                ExpenseCategoryId = expenseCategory.Id
            });

            // Opening float for the drawer: 100.
            // Expected = 100 + 500 - 200 = 400. Operator counted 380 → shortage 20.
            var result = await _closingService.PerformDailyClosingAsync(new CreateDailyClosingDto
            {
                BusinessDate = DateTime.Today,
                OpeningCashAmount = 100m,
                CountedCashAmount = 380m,
                Notes = "إقفال اعتيادي"
            });
            Assert.True(result);

            var closing = await _closingService.GetClosingByDateAsync(DateTime.Today);
            Assert.NotNull(closing);
            Assert.True(closing!.IsClosed);
            Assert.Equal(100m, closing.OpeningCashAmount);
            // Invariance: the persisted Expected equals the day's fresh recomputation
            // (opening + in - out). Preview totals are computed live from CashTransactions;
            // OpeningCashAmount is the operator's float value persisted with the closing.
            var preview = await _closingService.CalculatePreviewClosingAsync(DateTime.Today);
            Assert.Equal(500m, preview.TotalCashIn);
            Assert.Equal(200m, preview.TotalCashOut);

            var recomputedExpected = closing.OpeningCashAmount + preview.TotalCashIn - preview.TotalCashOut;
            Assert.Equal(400m, recomputedExpected);
            Assert.Equal(recomputedExpected, closing.ExpectedClosingCashAmount);
            Assert.Equal(380m, closing.CountedClosingCashAmount);
            Assert.Equal(-20m, closing.DifferenceAmount);          // 380 - 400

            // Re-closing a locked day is rejected until it is explicitly reopened.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _closingService.PerformDailyClosingAsync(new CreateDailyClosingDto
                {
                    BusinessDate = DateTime.Today,
                    OpeningCashAmount = 100m,
                    CountedCashAmount = 400m
                }));

            // Reopen with a mandatory reason, then re-close: the same invariant is
            // recomputed with the corrected counted cash.
            await _closingService.ReopenDailyClosingAsync(new BLL.DTOs.DailyClosing.InComingData.ReopenDailyClosingDto
            {
                DailyClosingId = closing!.Id,
                Reason = "خطأ جرد صندوق - تم الاكتشاف بعد الإقفال"
            });
            Assert.False(await _closingService.IsDateClosedAsync(DateTime.Today));

            await _closingService.PerformDailyClosingAsync(new CreateDailyClosingDto
            {
                BusinessDate = DateTime.Today,
                OpeningCashAmount = 100m,
                CountedCashAmount = 400m
            });
            closing = await _closingService.GetClosingByDateAsync(DateTime.Today);
            Assert.True(closing!.IsClosed);
            var previewAfterReopen = await _closingService.CalculatePreviewClosingAsync(DateTime.Today);
            Assert.Equal(closing.OpeningCashAmount + previewAfterReopen.TotalCashIn - previewAfterReopen.TotalCashOut,
                         closing.ExpectedClosingCashAmount);
            Assert.Equal(0m, closing.DifferenceAmount);
        }

        [Fact]
        public async Task ClosingIsDateDefined_TodayClosed_YesterdayNotAffected()
        {
            var yesterday = DateTime.Today.AddDays(-1);
            await _cashService.RecordCashMovementAsync(new CreateCashMovementDto
            {
                TransactionDate = yesterday,
                Amount = 1000m,
                Category = CashCategory.RetailSale
            });
            await _closingService.PerformDailyClosingAsync(new CreateDailyClosingDto
            {
                BusinessDate = yesterday,
                OpeningCashAmount = 0m,
                CountedCashAmount = 1000m
            });

            Assert.True(await _closingService.IsDateClosedAsync(yesterday));
            Assert.False(await _closingService.IsDateClosedAsync(DateTime.Today));
        }

        public void Dispose() => _harness.Dispose();
    }
}
