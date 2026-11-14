using Accountant_sys.Models;
using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using BLL.DTOs.Vendor.InComingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class VendorController : Controller
    {
        private readonly IVendorService _vendorService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IOpeningBalanceService _openingBalanceService;

        public VendorController(
            IVendorService vendorService,
            IInvoiceService invoiceService,
            ICashTransactionService cashTransactionService,
            ICreditNoteService creditNoteService,
            IOpeningBalanceService openingBalanceService)
        {
            _vendorService = vendorService;
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
            // Operational listing defaults to ACTIVE vendors only; the status
            // filter (active/inactive/all) covers management.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var vendors = await _vendorService.GetVendorsByActiveStatusAsync(filter) ?? new List<VendorDto>();
            return View(vendors);
        }

        // ==========================================
        // DETAILS (with balance & cash flow summary)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(id);
            if (vendor == null) return NotFound();

            var ledger = await BuildVendorLedgerAsync(id, StatementBuilder.HistoryStart, StatementBuilder.HistoryEnd);
            ViewBag.StatementSummary = StatementBuilder.Build(ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor", null, null);
            return View(vendor);
        }

        // ==========================================
        // ACCOUNT STATEMENT (كشف حساب مورد)
        // mode: "summary"  = ledger summary rows
        //       "detailed" = per-invoice breakdown (items, prices, discounts, totals) + full ledger
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to, string? mode)
        {
            var activeVendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            ViewBag.Parties = new SelectList(activeVendors, "Id", "Name");

            ViewBag.Mode = string.Equals(mode, "detailed", StringComparison.OrdinalIgnoreCase)
                ? "detailed"
                : "summary";

            if (id == null || id <= 0)
                return View();

            var vendor = activeVendors.FirstOrDefault(v => v.Id == id)
                ?? (await _vendorService.GetAllVendorAsync() ?? new List<VendorDto>()).FirstOrDefault(v => v.Id == id);
            if (vendor == null) return NotFound();
            var fromDate = from ?? StatementBuilder.HistoryStart;
            var toDate = to ?? StatementBuilder.HistoryEnd;
            var ledger = await BuildVendorLedgerAsync(id.Value, fromDate, toDate);
            var model = StatementBuilder.Build(ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor", from, to);
            model.Mode = (string)ViewBag.Mode;

            if (ViewBag.Mode == "detailed")
            {
                ViewBag.DetailedInvoices = await _invoiceService.GetPostedInvoiceDetailsAsync(
                        InvoiceType.VendorPurchase, null, vendor.Id, from, to)
                    ?? new List<StatementInvoiceDetailDto>();
            }

            return View(model);
        }

        private async Task<List<StatementLine>> BuildVendorLedgerAsync(int vendorId, DateTime from, DateTime to)
        {
            var lines = new List<StatementLine>();
            var historyStart = StatementBuilder.HistoryStart;

            var openings = await _openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var opening = openings.FirstOrDefault(o => o.VendorId == vendorId);
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
                    Credit = opening.Amount
                });
            }

            var invoices = await _invoiceService.GetInvoicesAsync(InvoiceType.VendorPurchase, InvoiceStatus.Posted) ?? new List<InvoiceDto>();
            var vendorInvoices = invoices.Where(i => i.VendorId == vendorId).ToList();
            foreach (var invoice in vendorInvoices)
            {
                lines.Add(new StatementLine
                {
                    Date = invoice.TransactionDate,
                    Timestamp = invoice.CreatedAt,
                    Kind = StatementLineKind.Invoice,
                    DocumentId = invoice.Id,
                    DocumentNo = invoice.InvoiceNumber,
                    DocumentType = "فاتورة مشتريات",
                    Description = StatementBuilder.FormatInvoiceDescription(invoice, isVendor: true, isMaritime: false),
                    Notes = invoice.Notes,
                    Credit = invoice.TotalAmount
                });
            }

            var returns = await _creditNoteService.GetCreditNotesAsync(
                CreditNoteType.VendorReturn, CreditNoteStatus.Posted,
                historyStart, to) ?? new List<CreditNoteDto>();
            var invoiceIds = vendorInvoices.Select(i => i.Id).ToHashSet();
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
                    Debit = note.TotalAmount
                });
            }

            var payments = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.VendorPayment, CashDirection.Out) ?? new List<CashTransactionDto>();
            foreach (var payment in payments.Where(p => p.VendorId == vendorId))
            {
                lines.Add(new StatementLine
                {
                    Date = payment.TransactionDate,
                    Timestamp = payment.CreatedAt,
                    Kind = StatementLineKind.VendorPayment,
                    DocumentId = payment.Id,
                    DocumentNo = "VCH" + payment.Id.ToString("D6"),
                    DocumentType = "سند صرف (دفع للمورد)",
                    Description = StatementBuilder.FormatPaymentDescription(payment, isVendor: true),
                    Notes = payment.Notes,
                    CashTransaction = payment,
                    Debit = payment.Amount
                });
            }

            var refunds = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.VendorRefund, CashDirection.In) ?? new List<CashTransactionDto>();
            foreach (var refund in refunds.Where(p => p.VendorId == vendorId))
            {
                lines.Add(new StatementLine
                {
                    Date = refund.TransactionDate,
                    Timestamp = refund.CreatedAt,
                    Kind = StatementLineKind.VendorRefund,
                    DocumentId = refund.Id,
                    DocumentNo = "VRF" + refund.Id.ToString("D6"),
                    DocumentType = "سند استرداد نقدي من المورد",
                    Description = string.IsNullOrWhiteSpace(refund.Notes) ? "استرداد نقدي من المورد (مرتجع)" : refund.Notes.Trim(),
                    Notes = refund.Notes,
                    CashTransaction = refund,
                    Credit = refund.Amount
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
        public async Task<IActionResult> Create(CreateVendorDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _vendorService.CreateVendorAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إضافة المورد بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة المورد. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(id);
            if (vendor == null) return NotFound();

            var dto = new UpdateVendorDto
            {
                Id = vendor.Id,
                Name = vendor.Name,
                PhoneNumber = vendor.PhoneNumber,
                Address = vendor.Address,
                IsActive = vendor.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateVendorDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _vendorService.UpdateVendorAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم تحديث بيانات المورد بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث بيانات المورد. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _vendorService.ToggleStatusAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم تغيير حالة المورد بنجاح.";
            else
                TempData["ErrorMessage"] = "المورد غير موجود.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // DELETE (Soft delete via service)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _vendorService.DeleteVendorAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم إلغاء تنشيط المورد بنجاح.";
            else
                TempData["ErrorMessage"] = "المورد غير موجود.";

            return RedirectToAction(nameof(Index));
        }
    }
}
