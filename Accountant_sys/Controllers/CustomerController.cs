using Accountant_sys.Models;
using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Customer.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class CustomerController : Controller
    {
        private readonly ICustomerService _customerService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IOpeningBalanceService _openingBalanceService;

        public CustomerController(
            ICustomerService customerService,
            IInvoiceService invoiceService,
            ICashTransactionService cashTransactionService,
            ICreditNoteService creditNoteService,
            IOpeningBalanceService openingBalanceService)
        {
            _customerService = customerService;
            _invoiceService = invoiceService;
            _cashTransactionService = cashTransactionService;
            _creditNoteService = creditNoteService;
            _openingBalanceService = openingBalanceService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? status)
        {
            // Operational listing defaults to ACTIVE wholesale customers only;
            // the status filter (active/inactive/all) covers management.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var customers = await _customerService.GetCustomersByActiveStatusAsync(filter) ?? new List<CustomerDto>();
            return View(customers);
        }

        // ==========================================
        // DETAILS (with balance & cash flow summary)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);
            if (customer == null) return NotFound();

            var ledger = await BuildCustomerLedgerAsync(id, StatementBuilder.HistoryStart, StatementBuilder.HistoryEnd);
            ViewBag.StatementSummary = StatementBuilder.Build(ledger, isVendor: false, customer.Id, customer.Name, "Customer", null, null);
            return View(customer);
        }

        // ==========================================
        // ACCOUNT STATEMENT (كشف حساب عميل)
        // mode: "summary"  = ledger summary rows
        //       "detailed" = per-invoice breakdown (items, prices, discounts, totals) + full ledger
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to, string? mode)
        {
            var activeCustomers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            ViewBag.Parties = new SelectList(activeCustomers, "Id", "Name");

            ViewBag.Mode = string.Equals(mode, "detailed", StringComparison.OrdinalIgnoreCase)
                ? "detailed"
                : "summary";

            if (id == null || id <= 0)
                return View();

            var customer = activeCustomers.FirstOrDefault(c => c.Id == id)
                ?? (await _customerService.GetAllCustomersAsync() ?? new List<CustomerDto>()).FirstOrDefault(c => c.Id == id);
            if (customer == null) return NotFound();
            var fromDate = from ?? StatementBuilder.HistoryStart;
            var toDate = to ?? StatementBuilder.HistoryEnd;
            var ledger = await BuildCustomerLedgerAsync(id.Value, fromDate, toDate);
            var model = StatementBuilder.Build(ledger, isVendor: false, customer.Id, customer.Name, "Customer", from, to);
            model.Mode = (string)ViewBag.Mode;

            if (ViewBag.Mode == "detailed")
            {
                ViewBag.DetailedInvoices = await _invoiceService.GetPostedInvoiceDetailsAsync(
                        InvoiceType.WholesaleSales, customer.Id, null, from, to)
                    ?? new List<StatementInvoiceDetailDto>();
            }

            return View(model);
        }

        private async Task<List<StatementLine>> BuildCustomerLedgerAsync(int customerId, DateTime from, DateTime to)
        {
            var lines = new List<StatementLine>();
            var historyStart = StatementBuilder.HistoryStart;
            var openings = await _openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var opening = openings.FirstOrDefault(o => o.CustomerId == customerId);
            if (opening != null)
            {
                lines.Add(new StatementLine
                {
                    Date = opening.EffectiveDate,
                    Timestamp = opening.CreatedAt,
                    SortGroup = 0,
                    Kind = StatementLineKind.OpeningBalance,
                    DocumentId = opening.Id,
                    DocumentNo = "OP" + opening.Id.ToString("D4"),
                    DocumentType = "رصيد افتتاحي",
                    Description = StatementBuilder.FormatOpeningDescription(opening),
                    Notes = opening.Notes,
                    Debit = opening.Amount
                });
            }

            var invoices = await _invoiceService.GetInvoicesAsync(InvoiceType.WholesaleSales, InvoiceStatus.Posted) ?? new List<InvoiceDto>();
            var customerInvoices = invoices.Where(i => i.CustomerId == customerId).ToList();
            foreach (var invoice in customerInvoices)
            {
                lines.Add(new StatementLine
                {
                    Date = invoice.TransactionDate,
                    Timestamp = invoice.CreatedAt,
                    Kind = StatementLineKind.Invoice,
                    DocumentId = invoice.Id,
                    DocumentNo = invoice.InvoiceNumber,
                    DocumentType = "فاتورة جملة (بيع)",
                    Description = StatementBuilder.FormatInvoiceDescription(invoice, isVendor: false, isMaritime: false),
                    Notes = invoice.Notes,
                    Debit = invoice.TotalAmount
                });
            }

            var returns = await _creditNoteService.GetCreditNotesAsync(
                CreditNoteType.CustomerReturn, CreditNoteStatus.Posted,
                historyStart, to) ?? new List<CreditNoteDto>();
            var invoiceIds = customerInvoices.Select(i => i.Id).ToHashSet();
            foreach (var note in returns.Where(n => invoiceIds.Contains(n.OriginalInvoiceId)))
            {
                lines.Add(new StatementLine
                {
                    Date = note.TransactionDate,
                    Timestamp = note.CreatedAt,
                    Kind = StatementLineKind.CreditNoteReturn,
                    DocumentId = note.Id,
                    DocumentNo = note.CreditNoteNumber,
                    DocumentType = "إشعار مرتجع",
                    Description = StatementBuilder.FormatCreditNoteDescription(note),
                    Notes = note.Reason,
                    CreditNote = note,
                    Credit = note.TotalAmount
                });
            }

            var payments = await _cashTransactionService.GetTransactionsAsync(
                from: historyStart, to, CashCategory.CustomerPayment, CashDirection.In) ?? new List<CashTransactionDto>();
            foreach (var payment in payments.Where(p => p.CustomerId == customerId))
            {
                lines.Add(new StatementLine
                {
                    Date = payment.TransactionDate,
                    Timestamp = payment.CreatedAt,
                    Kind = StatementLineKind.CustomerReceipt,
                    DocumentId = payment.Id,
                    DocumentNo = "REC" + payment.Id.ToString("D6"),
                    DocumentType = "سند قبض (دفع من العميل)",
                    Description = StatementBuilder.FormatPaymentDescription(payment, isVendor: false),
                    Notes = payment.Notes,
                    CashTransaction = payment,
                    Credit = payment.Amount
                });
            }

            var refunds = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.CustomerRefund, CashDirection.Out) ?? new List<CashTransactionDto>();
            foreach (var refund in refunds.Where(p => p.CustomerId == customerId))
            {
                lines.Add(new StatementLine
                {
                    Date = refund.TransactionDate,
                    Timestamp = refund.CreatedAt,
                    Kind = StatementLineKind.CustomerRefund,
                    DocumentId = refund.Id,
                    DocumentNo = "RFD" + refund.Id.ToString("D6"),
                    DocumentType = "سند رد نقدي للعميل",
                    Description = string.IsNullOrWhiteSpace(refund.Notes) ? "رد نقدي للعميل (مرتجع)" : refund.Notes.Trim(),
                    Notes = refund.Notes,
                    CashTransaction = refund,
                    Debit = refund.Amount
                });
            }

            return lines;
        }

        // ==========================================
        // CREATE
        // ==========================================
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCustomerDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _customerService.CreateCustomerAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إضافة العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة العميل. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);
            if (customer == null) return NotFound();

            var dto = new UpdateCustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                PhoneNumber = customer.PhoneNumber,
                Address = customer.Address,
                IsActive = customer.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateCustomerDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _customerService.UpdateCustomerAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم تحديث بيانات العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث بيانات العميل. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _customerService.ToggleStatusAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم تغيير حالة العميل بنجاح.";
            else
                TempData["ErrorMessage"] = "العميل غير موجود.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // DELETE (Soft delete via service)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _customerService.DeleteCustomerAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم إلغاء تنشيط العميل بنجاح.";
            else
                TempData["ErrorMessage"] = "العميل غير موجود.";

            return RedirectToAction(nameof(Index));
        }
    }
}
