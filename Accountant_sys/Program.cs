using BLL.IService;
using System.Diagnostics;
using BLL.Mapping.AdminMapping.InCommingData;
using BLL.Mapping.CashTransMapping.InComingData;
using BLL.Mapping.CashTransMapping.OutGoingData;
using BLL.Mapping.CreditNotesMapping.OutGoingData;
using BLL.Mapping.CustomerMapping.InComingData;
using BLL.Mapping.CustomerMapping.OutGoingData;
using BLL.Mapping.ExpenseCategoryMapping.OutGoingData;
using BLL.Mapping.InvoiceMapping;
using BLL.Mapping.InvoiceMapping.InComingData;
using BLL.Mapping.InvoiceMapping.OutGoingData;
using BLL.Mapping.MaritimeCompanyMapping;
using BLL.Mapping.OpeningBalanceMapping.InComingData;
using BLL.Mapping.OpeningBalanceMapping.OutGoingData;
using BLL.Mapping.Payment.OutGoingData;
using BLL.Mapping.ProductMapping.InCommingData;
using BLL.Mapping.ProductMapping.OutGoingData;
using BLL.Mapping.VendorMapping.InComingData;
using BLL.Mapping.VendorMapping.OutGoingData;
using BLL.Service;
using DAL.AppDbContext;
using DAL.Entities;
using DAL.IConfiguration;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    // Global [Authorize] filter — all controllers require login by default
    options.Filters.Add(new AuthorizeFilter());
});

builder.Services.AddDbContext<AppDbcontext>(option =>
    option.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbcontext>()
.AddDefaultTokenProviders();

// Cookie configuration
builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Account/Login";
    opt.AccessDeniedPath = "/Account/AccessDenied";
    opt.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    opt.SlidingExpiration = true;
});

// Session
builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromMinutes(30);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
});

builder.Services.AddAutoMapper(cfg => { },
    typeof(RegisterAdminProfile),
    typeof(CreateCutomerMapping),
    typeof(UpdateCustomerMapping),
    typeof(DisplayCustomerMapping),
    typeof(CreateProductMapping),
    typeof(UpdateProductMapping),
    typeof(DisplayActiveProducts),
    typeof(CreateVendorMapping),
    typeof(UpdateVendorMapping),
    typeof(DisplayActiveVendors),
    typeof(CreateInvoiceProfile),
    typeof(DisplayInvoiceProfile),
    typeof(InvoiceItemProfile),
    typeof(OpenInvoicesForPaymentMapping),
    typeof(DisplayCashTransactionMapping),
    typeof(CreateCashTransactionProfile),
    typeof(DisplayCreditNotesProfile),
    typeof(DisplayOpeningBalanceProfile),
    typeof(CreateOpeningBalanceProfile),
    typeof(DisplayExpenseCategoryProfile),
    typeof(CreateCashTransactionProfile),
    typeof(PaymentAllocationProfile),
    typeof(CreditNoteItemProfile),
    typeof(MaritimeCompanyMapping));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICashTransactionService, CashTransactionService>();
builder.Services.AddScoped<ICreditNoteService, CreditNoteService>();
builder.Services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
builder.Services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
builder.Services.AddScoped<IDailyClosingService, DailyClosingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IStockMovementService, StockMovementService>();
builder.Services.AddScoped<IMaritimeCompanyService, MaritimeCompanyService>();
builder.Services.AddScoped<IBackupService, BackupService>();

var app = builder.Build();

// Desktop-style startup: open the operator's browser automatically when the app
// starts outside a development IDE (single-user machine, no external hosting).
if (!app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var startUrl = app.Urls.FirstOrDefault(u => u != null && u.StartsWith("http://"))
                           ?? "http://localhost:5000";
            Process.Start(new ProcessStartInfo(startUrl) { UseShellExecute = true });
        }
        catch
        {
            // Missing/default browser is not fatal — the server keeps running.
        }
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
