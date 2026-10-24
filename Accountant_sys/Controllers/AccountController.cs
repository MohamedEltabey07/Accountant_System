using BLL.DTOs.Auth;
using BLL.IService;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Accountant_sys.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly UserManager<AppUser> _userManager;

        public AccountController(IAuthService authService, UserManager<AppUser> userManager)
        {
            _authService = authService;
            _userManager = userManager;
        }

        // ==========================================
        // LOGIN
        // ==========================================
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }

            // If no admin exists, redirect to first-time setup
            if (!await _authService.HasAnyAdminAsync())
                return RedirectToAction(nameof(RegisterFirstAdmin));

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDto dto, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _authService.LoginAsync(dto);
            if (result)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "البريد الإلكتروني أو كلمة المرور غير صحيحة");
            return View(dto);
        }

        // ==========================================
        // REGISTER FIRST ADMIN (First-time setup)
        // ==========================================
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> RegisterFirstAdmin()
        {
            // Block if an admin already exists
            if (await _authService.HasAnyAdminAsync())
                return RedirectToAction(nameof(Login));

            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterFirstAdmin(RegisterAdminDto dto)
        {
            // Block if an admin already exists
            if (await _authService.HasAnyAdminAsync())
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View(dto);

            var result = await _authService.RegisterFirstAdminAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إنشاء حساب المسؤول بنجاح. يمكنك تسجيل الدخول الآن.";
                return RedirectToAction(nameof(Login));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إنشاء الحساب. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // LOGOUT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _authService.LogoutAsync();
            return RedirectToAction(nameof(Login));
        }

        // ==========================================
        // CHANGE PASSWORD
        // ==========================================
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var userId = _userManager.GetUserId(User);
            if (userId == null)
                return RedirectToAction(nameof(Login));

            var result = await _authService.ChangePasswordAsync(userId, dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم تغيير كلمة المرور بنجاح.";
                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "كلمة المرور الحالية غير صحيحة.");
            return View(dto);
        }

        // ==========================================
        // ACCESS DENIED
        // ==========================================
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
