using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.OpeningBalance.InComingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class OpeningBalanceController : Controller
    {
        private readonly IOpeningBalanceService _openingBalanceService;
        private readonly ICustomerService _customerService;
        private readonly IVendorService _vendorService;

        public OpeningBalanceController(
            IOpeningBalanceService openingBalanceService,
            ICustomerService customerService,
            IVendorService vendorService)
        {
            _openingBalanceService = openingBalanceService;
            _customerService = customerService;
            _vendorService = vendorService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var balances = await _openingBalanceService.GetAllOpeningBalancesAsync();
            return View(balances ?? new List<OpeningBalanceDto>());
        }

        // ==========================================
        // CREATE CUSTOMER OPENING BALANCE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> CreateCustomerBalance()
        {
            await RepopulateCustomerSelectListAsync();
            return View(new CreateOpeningBalanceDto { EffectiveDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCustomerBalance(CreateOpeningBalanceDto dto)
        {
            dto.VendorId = null;

            if (!ModelState.IsValid)
            {
                await RepopulateCustomerSelectListAsync();
                return View(dto);
            }

            try
            {
                var result = await _openingBalanceService.SetCustomerOpeningBalanceAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم تسجيل الرصيد الافتتاحي للعميل بنجاح.";
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تسجيل الرصيد الافتتاحي للعميل. تأكد من البيانات وحاول مرة أخرى.");
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات الرصيد الافتتاحي غير مكتملة." : ex.Message);
            }
            catch (NullReferenceException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الرصيد الافتتاحي." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الرصيد الافتتاحي." : ex.Message);
            }

            await RepopulateCustomerSelectListAsync();
            return View(dto);
        }

        // ==========================================
        // CREATE VENDOR OPENING BALANCE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> CreateVendorBalance()
        {
            await RepopulateVendorSelectListAsync();
            return View(new CreateOpeningBalanceDto { EffectiveDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVendorBalance(CreateOpeningBalanceDto dto)
        {
            dto.CustomerId = null;

            if (!ModelState.IsValid)
            {
                await RepopulateVendorSelectListAsync();
                return View(dto);
            }

            try
            {
                var result = await _openingBalanceService.SetVendorOpeningBalanceAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم تسجيل الرصيد الافتتاحي للمورد بنجاح.";
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تسجيل الرصيد الافتتاحي للمورد. تأكد من البيانات وحاول مرة أخرى.");
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات الرصيد الافتتاحي غير مكتملة." : ex.Message);
            }
            catch (NullReferenceException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الرصيد الافتتاحي." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تسجيل الرصيد الافتتاحي." : ex.Message);
            }

            await RepopulateVendorSelectListAsync();
            return View(dto);
        }

        // ==========================================
        // PRIVATE HELPERS
        // ==========================================
        private async Task<List<OpeningBalanceDto>> GetAllBalancesSafeAsync()
        {
            var balances = await _openingBalanceService.GetAllOpeningBalancesAsync();
            return balances ?? new List<OpeningBalanceDto>();
        }

        private async Task RepopulateCustomerSelectListAsync()
        {
            var balances = await GetAllBalancesSafeAsync();
            var customers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            var withoutBalance = customers
                .Where(c => !balances.Any(ob => ob.CustomerId == c.Id))
                .ToList();
            ViewBag.Customers = new SelectList(withoutBalance, "Id", "Name");
        }

        private async Task RepopulateVendorSelectListAsync()
        {
            var balances = await GetAllBalancesSafeAsync();
            var vendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            var withoutBalance = vendors
                .Where(v => !balances.Any(ob => ob.VendorId == v.Id))
                .ToList();
            ViewBag.Vendors = new SelectList(withoutBalance, "Id", "Name");
        }
    }
}
