using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Product.OutGoingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class InvoiceController : Controller
    {
        private readonly IInvoiceService _invoiceService;
        private readonly IProductService _productService;
        private readonly ICustomerService _customerService;
        private readonly IVendorService _vendorService;

        public InvoiceController(IInvoiceService invoiceService, IProductService productService,
            ICustomerService customerService, IVendorService vendorService)
        {
            _invoiceService = invoiceService;
            _productService = productService;
            _customerService = customerService;
            _vendorService = vendorService;
        }

        // ==========================================
        // LIST (filterable by type & status)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? type, string? status)
        {
            InvoiceType? parsedType = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (Enum.TryParse<InvoiceType>(type, true, out var t))
                    parsedType = t;
                else if (int.TryParse(type, out var tInt) && Enum.IsDefined(typeof(InvoiceType), tInt))
                    parsedType = (InvoiceType)tInt;
            }

            InvoiceStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (Enum.TryParse<InvoiceStatus>(status, true, out var s))
                    parsedStatus = s;
                else if (int.TryParse(status, out var sInt) && Enum.IsDefined(typeof(InvoiceStatus), sInt))
                    parsedStatus = (InvoiceStatus)sInt;
            }

            ViewBag.SelectedType = parsedType;
            ViewBag.SelectedStatus = parsedStatus;

            var invoices = await _invoiceService.GetInvoicesAsync(parsedType, parsedStatus);
            return View(invoices);
        }

        // ==========================================
        // DETAILS (draft management + posted view)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            await PrepareProductSelectList();
            return View(invoice);
        }

        // ==========================================
        // CREATE DRAFT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create(InvoiceType? type, int? customerId, int? vendorId)
        {
            await PreparePartySelectLists();
            var dto = new CreateInvoiceDraftDto
            {
                Type = type ?? InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customerId,
                VendorId = vendorId
            };
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateInvoiceDraftDto dto)
        {
            await PreparePartySelectLists();

            if (!ModelState.IsValid)
                return View(dto);

            var draftId = await _invoiceService.CreateDraftInvoiceAsync(dto);
            if (draftId > 0)
                return RedirectToAction(nameof(Details), new { id = draftId });

            ModelState.AddModelError(string.Empty, "تحقق من نوع الفاتورة: فاتورة الشراء تتطلب موردًا، فاتورة الجملة تتطلب عميلًا، والبيع بالتجزئة بدون طرف.");
            return View(dto);
        }

        // ==========================================
        // ADD ITEM TO DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem(AddInvoiceItemDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "بيانات البند غير صحيحة.";
                return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
            }

            var result = await _invoiceService.AddItemToDraftAsync(dto);
            if (!result)
            {
                TempData["ErrorMessage"] = "تعذر إضافة البند. تأكد من أن الفاتورة مسودة والمنتج موجود.";
            }

            return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
        }

        // ==========================================
        // UPDATE ITEM ON DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItem(UpdateInvoiceItemDto dto, int invoiceId)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "بيانات البند غير صحيحة.";
                return RedirectToAction(nameof(Details), new { id = invoiceId });
            }

            var result = await _invoiceService.UpdateDraftItemAsync(dto);
            if (!result)
            {
                TempData["ErrorMessage"] = "تعذر تعديل البند. تأكد من أن الفاتورة مسودة.";
            }

            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }

        // ==========================================
        // REMOVE ITEM FROM DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveItem(int itemId, int invoiceId)
        {
            var result = await _invoiceService.RemoveItemFromDraftAsync(itemId);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] = result
                ? "تم حذف البند بنجاح."
                : "تعذر حذف البند. تأكد من أن الفاتورة مسودة.";

            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }

        // ==========================================
        // POST INVOICE (freezes financial snapshot)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id)
        {
            var result = await _invoiceService.PostInvoiceAsync(id);
            if (result)
            {
                TempData["SuccessMessage"] = "تم ترحيل الفاتورة بنجاح.";
            }
            else
            {
                TempData["ErrorMessage"] = "تعذر ترحيل الفاتورة. يجب أن تكون مسودة وتحتوي على بند واحد على الأقل.";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ==========================================
        // CANCEL POSTED INVOICE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Cancel(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            if (invoice.Status != InvoiceStatus.Posted)
            {
                TempData["ErrorMessage"] = "لا يمكن إلغاء إلا الفواتير المرحلة فقط.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new CancelInvoiceDto { InvoiceId = invoice.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(CancelInvoiceDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _invoiceService.CancelPostedInvoiceAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إلغاء الفاتورة بنجاح.";
                return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
            }

            ModelState.AddModelError(string.Empty, "تعذر إلغاء الفاتورة. لا يمكن إلغاء فاتورة لها مدفوعات أو مرتجعات مسجلة.");
            return View(dto);
        }

        // ==========================================
        // REMAINING BALANCE / PAYMENT STATUS (AJAX-friendly)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> RemainingBalance(int id)
        {
            var remaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(id);
            return Json(remaining);
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private async Task PreparePartySelectLists()
        {
            var customers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            var vendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();

            ViewBag.Customers = new SelectList(customers, "Id", "Name");
            ViewBag.Vendors = new SelectList(vendors, "Id", "Name");
        }

        private async Task PrepareProductSelectList()
        {
            var products = await _productService.GetActiveProductsAsync() ?? new List<ProductDto>();
            ViewBag.Products = new SelectList(products, "Id", "Name");
        }

        private static SelectList GetInvoiceTypeSelectList(InvoiceType? selected = null)
        {
            return new SelectList(new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "كل الأنواع" },
                new SelectListItem { Value = ((int)InvoiceType.VendorPurchase).ToString(), Text = "مشتريات من مورد" },
                new SelectListItem { Value = ((int)InvoiceType.WholesaleSales).ToString(), Text = "بيع بالجملة" },
                new SelectListItem { Value = ((int)InvoiceType.StoreCashSales).ToString(), Text = "بيع بالتجزئة" }
            }, "Value", "Text", selected.HasValue ? (int?)selected.Value : null);
        }

        private static SelectList GetInvoiceStatusSelectList(InvoiceStatus? selected = null)
        {
            return new SelectList(new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "كل الحالات" },
                new SelectListItem { Value = ((int)InvoiceStatus.Draft).ToString(), Text = "مسودة" },
                new SelectListItem { Value = ((int)InvoiceStatus.Posted).ToString(), Text = "مرحلة" },
                new SelectListItem { Value = ((int)InvoiceStatus.Cancelled).ToString(), Text = "ملغاة" }
            }, "Value", "Text", selected.HasValue ? (int?)selected.Value : null);
        }
    }
}
