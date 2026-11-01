using BLL.DTOs.Cash.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.ExpenseCategory.OutGoingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using BLL.DTOs.Payment.InComingData;
using BLL.DTOs.Payment.OutGoingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class PaymentController : Controller
    {
        private readonly IPaymentService _paymentService;
        private readonly ICustomerService _customerService;
        private readonly IVendorService _vendorService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly IMaritimeCompanyService _maritimeCompanyService;
        private readonly IExpenseCategoryService _expenseCategoryService;

        public PaymentController(
            IPaymentService paymentService,
            ICustomerService customerService,
            IVendorService vendorService,
            ICashTransactionService cashTransactionService,
            IMaritimeCompanyService maritimeCompanyService,
            IExpenseCategoryService expenseCategoryService)
        {
            _paymentService = paymentService;
            _customerService = customerService;
            _vendorService = vendorService;
            _cashTransactionService = cashTransactionService;
            _maritimeCompanyService = maritimeCompanyService;
            _expenseCategoryService = expenseCategoryService;
        }

        // ==========================================
        // PAYMENTS LANDING PAGE
        // ==========================================
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // ==========================================
        // CUSTOMER PAYMENT (سند قبض)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> CustomerPayment()
        {
            await PrepareCustomerSelectListAsync();
            return View(new CreateCustomerPaymentDto { PaymentDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CustomerPayment(CreateCustomerPaymentDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PrepareCustomerSelectListAsync();
                return View(dto);
            }

            try
            {
                var transactionId = await _paymentService.RecordCustomerPaymentAsync(dto);
                TempData["SuccessMessage"] = "تم تسجيل سند القبض وتوزيع الدفعة بنجاح.";
                return RedirectToAction(nameof(Receipt), new { id = transactionId });
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات الدفعة غير مكتملة." : ex.Message);
            }
            catch (NullReferenceException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الدفعة." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الدفعة." : ex.Message);
            }

            await PrepareCustomerSelectListAsync();
            return View(dto);
        }

        // AJAX: open invoices of a customer
        [HttpGet]
        public async Task<IActionResult> GetCustomerOpenInvoices(int customerId)
        {
            if (customerId <= 0)
                return Json(new List<OpenInvoiceForPaymentDto>());

            var invoices = await _paymentService.GetCustomerOpenInvoicesAsync(customerId);
            return Json(invoices ?? new List<OpenInvoiceForPaymentDto>());
        }

        // ==========================================
        // VENDOR PAYMENT (سند صرف)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> VendorPayment()
        {
            await PrepareVendorSelectListAsync();
            return View(new CreateVendorPaymentDto { PaymentDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VendorPayment(CreateVendorPaymentDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PrepareVendorSelectListAsync();
                return View(dto);
            }

            try
            {
                var transactionId = await _paymentService.RecordVendorPaymentAsync(dto);
                TempData["SuccessMessage"] = "تم تسجيل سند الصرف وتوزيع الدفعة بنجاح.";
                return RedirectToAction(nameof(Receipt), new { id = transactionId });
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات الدفعة غير مكتملة." : ex.Message);
            }
            catch (NullReferenceException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الدفعة." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الدفعة." : ex.Message);
            }

            await PrepareVendorSelectListAsync();
            return View(dto);
        }

        // AJAX: open invoices of a vendor
        [HttpGet]
        public async Task<IActionResult> GetVendorOpenInvoices(int vendorId)
        {
            if (vendorId <= 0)
                return Json(new List<OpenInvoiceForPaymentDto>());

            var invoices = await _paymentService.GetVendorOpenInvoicesAsync(vendorId);
            return Json(invoices ?? new List<OpenInvoiceForPaymentDto>());
        }

        // ==========================================
        // EXPENSE PAYMENT (سند صرف مصروفات تشغيلية)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Expense()
        {
            await PrepareExpenseCategorySelectListAsync();
            return View(new CreateCashMovementDto
            {
                TransactionDate = DateTime.Today,
                Category = CashCategory.Expense,
                Direction = CashDirection.Out
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Expense(CreateCashMovementDto dto)
        {
            dto.Category = CashCategory.Expense;
            dto.Direction = CashDirection.Out;

            if (!ModelState.IsValid)
            {
                await PrepareExpenseCategorySelectListAsync();
                return View(dto);
            }

            try
            {
                var transactionId = await _cashTransactionService.RecordExpenseAsync(dto);
                TempData["SuccessMessage"] = "تم تسجيل سند صرف المصروف بنجاح وخصم المبلغ من الخزينة.";
                return RedirectToAction(nameof(Receipt), new { id = transactionId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await PrepareExpenseCategorySelectListAsync();
            return View(dto);
        }

        // ==========================================
        // RECEIPT / VOUCHER (printable)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Receipt(int id)
        {
            var transaction = await _cashTransactionService.GetTransactionByIdAsync(id);
            if (transaction == null)
                return NotFound();

            ViewBag.VoucherTitle = transaction.Category == CashCategory.VendorPayment
                ? "سند صرف - مورد"
                : (transaction.Category == CashCategory.Expense ? "سند صرف مصروفات تشغيلية" : "سند قبض - عميل");

            return View(transaction);
        }

        // ==========================================
        // PRIVATE HELPERS
        // ==========================================
        private async Task PrepareExpenseCategorySelectListAsync()
        {
            var categories = await _expenseCategoryService.GetActiveCategoriesAsync() ?? new List<ExpenseCategoryDto>();
            ViewBag.ExpenseCategories = new SelectList(categories, "Id", "Name");
        }
        private async Task PrepareCustomerSelectListAsync()
        {
            var customers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            // Maritime companies receive cash receipts too (same CashReceiptPayment
            // flow); merge them into the same payer list with a marker so the user
            // can tell them apart at a glance.
            var maritimeCompanies = (await _maritimeCompanyService.GetActiveCompaniesAsync())
                ?.Select(m => new CustomerDto { Id = m.Id, Name = $"{m.Name} (شركة بحرية)", CurrentBalance = m.OutstandingBalance })
                ?? new List<CustomerDto>();
            ViewBag.Customers = new SelectList(customers.Concat(maritimeCompanies), "Id", "Name");
        }

        private async Task PrepareVendorSelectListAsync()
        {
            var vendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            ViewBag.Vendors = new SelectList(vendors, "Id", "Name");
        }
    }
}
