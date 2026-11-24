using System.Reflection;
using System.Text;
using Accountant_sys.Models;
using Accountant_sys.Tests.Helpers;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Payment.InComingData;
using BLL.IService;
using BLL.Service;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Accountant_sys.Tests
{
    /// <summary>
    /// Renders the real Razor statement sheet (precompiled views) outside a web
    /// host so the markup can be asserted and, when ST_PREVIEW_DIR is set,
    /// written to disk for Chrome screenshot / print-to-PDF inspection.
    /// </summary>
    public class StatementViewRenderTests : IDisposable
    {
        private readonly TestHarness _harness;
        private readonly CashTransactionService _cashService;
        private readonly OpeningBalanceService _openingService;

        public StatementViewRenderTests()
        {
            _harness = new TestHarness();
            _cashService = new CashTransactionService(_harness.UnitOfWork, _harness.Mapper);
            _openingService = new OpeningBalanceService(_harness.UnitOfWork, _harness.Mapper);
        }

        [Fact]
        public async Task AcceptanceScenario_RendersOneFlatMovementTableAndUnpaidSection()
        {
            var model = await SeedAcceptanceScenarioAsync();
            var html = await StatementViewRenderer.RenderAsync(model);

            Assert.Contains("id=\"statementLedgerTable\"", html);
            Assert.Equal(1, CountOccurrences(html, "id=\"statementLedgerTable\""));
            Assert.Contains("id=\"unpaidSection\"", html);
            Assert.Contains("id=\"stToolbar\"", html);
            Assert.Contains("d-print-none", html);

            // 26 movements, no synthetic carry-forward (unfiltered statement).
            Assert.Equal(26, CountOccurrences(html, "<tbody class=\"st-mv-group"));
            Assert.Equal(17, CountOccurrences(html, "data-jump-invoice="));

            // Opening row appears exactly once and is labelled with the new wording.
            Assert.Equal(1, CountOccurrences(html, "st-badge-opening"));
            Assert.Contains("رصيد أول المدة", html);

            // Every acceptance total, to the cent.
            Assert.Contains("99,700.00", html);
            Assert.Contains("171,366.00", html);
            Assert.Contains("175,200.00", html);
            Assert.Contains("88,866.00", html);
            Assert.Contains("92,700.00", html);
            Assert.Contains("95,866.00", html);

            // Western digits only, LTR isolation for amounts.
            Assert.DoesNotContain("٠", html, StringComparison.Ordinal);
            Assert.DoesNotContain("٩", html, StringComparison.Ordinal);
            Assert.Contains("class=\"st-num st-bal num-ltr\"", html);

            // Structured product line-items card with per-product row totals.
            Assert.Contains("class=\"st-items-card\"", html);
            Assert.Contains("class=\"st-item-total\"", html);
            Assert.Contains("325.00", html);    // 2.5 × 130.00 on PUR-0020
            Assert.Contains("17,100.00", html); // 30 × 570.00 on PUR-0020

            // The movements section must contain exactly one table (no nested tables).
            var movements = Between(html, "id=\"movementsSection\"", "id=\"unpaidSection\"");
            Assert.Equal(1, CountOccurrences(movements, "<table"));

            // The root cause fix: no HTML entities inside the emitted page CSS.
            var pageCss = Between(html, "<style>", "</style>");
            Assert.DoesNotContain("&#", pageCss, StringComparison.Ordinal);
            Assert.Contains("counter(page)", pageCss);

            // Long internal payment note is not printed as a public note.
            Assert.DoesNotContain("هيبقوا 8900", html);

            var previewDir = Environment.GetEnvironmentVariable("ST_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(previewDir))
            {
                Directory.CreateDirectory(previewDir);
                await File.WriteAllTextAsync(
                    Path.Combine(previewDir, "statement-vendor.html"),
                    StatementViewRenderer.WrapStandalone(html),
                    new UTF8Encoding(false));
            }
        }

        [Fact]
        public async Task MultiItemCustomerAndMaritimeStatements_RenderStructuredProductSectionWithTotals()
        {
            var customer = _harness.SeedWholesaleCustomer("عميل الأسماك البحرية");
            var items = new[]
            {
                (_harness.SeedRegularProduct("سمك فيليه ابيض").Id, 20m, 140m),
                (_harness.SeedRegularProduct("سلمون كامل مجمد").Id, 12m, 560m),
                (_harness.SeedRegularProduct("سمك فيليه احمر").Id, 20m, 130m),
                (_harness.SeedRegularProduct("كابوريا مجمده").Id, 20m, 140m),
                (_harness.SeedRegularProduct("جمبري مقشر").Id, 8m, 850m),
                (_harness.SeedRegularProduct("ماكريل").Id, 15m, 110m),
                (_harness.SeedRegularProduct("كاليماري حلقات").Id, 10m, 230m),
                (_harness.SeedRegularProduct("كاليماري حلقات بالبقسماط").Id, 5m, 220m)
            };

            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.WholesaleSales,
                TransactionDate = new DateTime(2026, 11, 20),
                CustomerId = customer.Id
            });
            foreach (var (productId, qty, price) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price
                });
            }
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));

            var customerModel = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.Customer, customer.Id, customer.Name, "Customer",
                null, null, "detailed", "01099999999", "بورسعيد",
                _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            var customerHtml = await StatementViewRenderer.RenderAsync(customerModel);
            Assert.Equal(8, CountOccurrences(customerHtml, "class=\"st-item-row\""));
            Assert.Contains("<span class=\"st-ih-qty\">الوزن</span>", customerHtml);
            Assert.Contains("2,800.00", customerHtml);
            Assert.Contains("6,720.00", customerHtml);
            Assert.Contains("2,600.00", customerHtml);
            Assert.Contains("6,800.00", customerHtml);
            Assert.Contains("1,650.00", customerHtml);
            Assert.Contains("2,300.00", customerHtml);
            Assert.Contains("1,100.00", customerHtml);

            // Maritime company statement uses "الوزن (كجم)" header and renders row totals
            var company = _harness.SeedMaritimeCompany("شركة البحر المتوسط الملاحية");
            var frozen = _harness.SeedFrozenFish("جمبري جامبو", 10m, 850m);
            await _harness.CreateStockInAsync(frozen.Id, 5m);
            var marDraft = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.MaritimeSupply,
                TransactionDate = new DateTime(2026, 11, 21),
                SupplyOrderDate = new DateTime(2026, 11, 21),
                CustomerId = company.Id,
                ExternalOrderNumber = "SEA-7788"
            });
            await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
            {
                InvoiceId = marDraft,
                ProductId = frozen.Id,
                Quantity = 8m,
                UnitPrice = 850m,
                IsFromFreezer = true
            });
            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(marDraft));

            var maritimeModel = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.MaritimeCompany, company.Id, company.Name, "MaritimeCompany",
                null, null, "detailed", "01288888888", "السويس",
                _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            var maritimeHtml = await StatementViewRenderer.RenderAsync(maritimeModel);
            Assert.Contains("<span class=\"st-ih-qty\">الوزن (كجم)</span>", maritimeHtml);
            Assert.Contains("6,800.00", maritimeHtml);

            var previewDir = Environment.GetEnvironmentVariable("ST_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(previewDir))
            {
                Directory.CreateDirectory(previewDir);
                await File.WriteAllTextAsync(
                    Path.Combine(previewDir, "statement-customer-8items.html"),
                    StatementViewRenderer.WrapStandalone(customerHtml),
                    new UTF8Encoding(false));
                await File.WriteAllTextAsync(
                    Path.Combine(previewDir, "statement-maritime.html"),
                    StatementViewRenderer.WrapStandalone(maritimeHtml),
                    new UTF8Encoding(false));
            }
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static string Between(string text, string start, string end)
        {
            var from = text.IndexOf(start, StringComparison.Ordinal);
            if (from < 0) return string.Empty;
            var to = text.IndexOf(end, from, StringComparison.Ordinal);
            return to < 0 ? text[from..] : text[from..to];
        }

        private async Task<StatementViewModel> SeedAcceptanceScenarioAsync()
        {
            var vendor = new Vendor
            {
                Name = "علي المصري",
                IsActive = true,
                PhoneNumber = "01012345678",
                Address = "الإسكندرية"
            };
            await _harness.UnitOfWork.Vendors.AddAsync(vendor);
            await _harness.UnitOfWork.SaveChangesAsync();

            var mackerel = _harness.SeedRegularProduct("ماكريل");
            var shrimp = _harness.SeedRegularProduct("جمبري مقشر");
            var salmon = _harness.SeedRegularProduct("سلمون");
            var fillet = _harness.SeedRegularProduct("سمك فيليه ابيض");
            var calamari = _harness.SeedRegularProduct("كاليماري");
            var morgan = _harness.SeedRegularProduct("مرجان احمر");
            var crabs = _harness.SeedRegularProduct("كابوريا مجمده");
            var bouri = _harness.SeedRegularProduct("بوري");

            var pur24Id = await PostVendorPurchaseAsync(vendor.Id, new DateTime(2026, 11, 10), (mackerel.Id, 500m, 70m, 0m));

            _harness.Context.OpeningBalanceEntries.Add(new OpeningBalanceEntry
            {
                VendorId = vendor.Id,
                Amount = 99700m,
                EffectiveDate = new DateTime(2026, 11, 11),
                CreatedAt = DateTime.UtcNow
            });
            await _harness.Context.SaveChangesAsync();

            var d11 = new DateTime(2026, 11, 11);
            await PostVendorPurchaseAsync(vendor.Id, d11, (shrimp.Id, 4m, 280m, 0m));
            var pur04Id = await PostVendorPurchaseAsync(vendor.Id, d11, (mackerel.Id, 50m, 650m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 20.48m, 450m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 10.30m, 450m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 140m, 105m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 20m, 105m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 135m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 135m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 20m, 105m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 140m, 110m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (fillet.Id, 10m, 110m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 130m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 130m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (morgan.Id, 50m, 150m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 10m, 130m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (salmon.Id, 21.84m, 500m, 0m));
            await PostVendorPurchaseAsync(vendor.Id, d11, (shrimp.Id, 10m, 400m, 0m));
            var pur20Id = await PostVendorPurchaseAsync(vendor.Id, d11, (crabs.Id, 2.5m, 130m, 0m), (shrimp.Id, 30m, 570m, 0m));
            var pur21Id = await PostVendorPurchaseAsync(vendor.Id, d11, (calamari.Id, 20m, 130m, 0m), (bouri.Id, 20m, 90m, 0m));

            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 63000m,
                Notes = "حساب مرجان خالص بيد علي المصري",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 63000m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 20800m,
                Notes = "دفعة حساب كاستيلا خالص",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 20800m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 32500m,
                Notes = "دفعة ماكريل خالص",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = pur04Id, AllocatedAmount = 32500m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = d11,
                Amount = 8900m,
                Notes = "المفروض يكون 9100 عشان الجمبري الي علي اخده 13x700 = 9100 و كان هو دافع 200 للسواق عشان بضاعه للمحل ف هخصمهم من ال 9100 ف هيبقوا 8900",
                Allocations = new List<InvoiceAllocationItemDto> { new() { InvoiceId = 0, AllocatedAmount = 8900m } }
            });
            await _harness.PaymentService.RecordVendorPaymentAsync(new CreateVendorPaymentDto
            {
                VendorId = vendor.Id,
                PaymentDate = new DateTime(2026, 11, 17),
                Amount = 50000m,
                Notes = "تحصيل دفعة على 3 فواتير",
                Allocations = new List<InvoiceAllocationItemDto>
                {
                    new() { InvoiceId = pur20Id, AllocatedAmount = 17425m },
                    new() { InvoiceId = pur21Id, AllocatedAmount = 4400m },
                    new() { InvoiceId = pur24Id, AllocatedAmount = 28175m }
                }
            });

            var model = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.Vendor, vendor.Id, vendor.Name, "Vendor",
                null, null, "detailed", vendor.PhoneNumber, vendor.Address,
                _openingService, _harness.InvoiceService, _harness.CreditNoteService, _cashService);

            Assert.Equal(95866m, model.EndingBalance);
            Assert.Equal(88866m, model.UnpaidTotal);
            Assert.Equal(92700m, model.OnAccountTotal);
            return model;
        }

        private async Task<int> PostVendorPurchaseAsync(
            int vendorId, DateTime date, params (int ProductId, decimal Qty, decimal Price, decimal Discount)[] items)
        {
            var draftId = await _harness.InvoiceService.CreateDraftInvoiceAsync(new CreateInvoiceDraftDto
            {
                Type = InvoiceType.VendorPurchase,
                TransactionDate = date,
                VendorId = vendorId
            });

            foreach (var (productId, qty, price, discount) in items)
            {
                await _harness.InvoiceService.AddItemToDraftAsync(new AddInvoiceItemDto
                {
                    InvoiceId = draftId,
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = price,
                    LineDiscountAmount = discount
                });
            }

            Assert.True(await _harness.InvoiceService.PostInvoiceAsync(draftId));
            return draftId;
        }

        public void Dispose() => _harness.Dispose();
    }

    internal static class StatementViewRenderer
    {
        private static readonly string ContentRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Accountant_sys"));

        public static async Task<string> RenderAsync(StatementViewModel model)
        {
            var webRoot = Path.Combine(ContentRoot, "wwwroot");
            var env = new RenderEnvironment
            {
                ApplicationName = "Accountant_sys",
                ContentRootPath = ContentRoot,
                ContentRootFileProvider = new PhysicalFileProvider(ContentRoot),
                WebRootPath = webRoot,
                WebRootFileProvider = new PhysicalFileProvider(webRoot)
            };

            var services = new ServiceCollection();
            services.AddSingleton<IWebHostEnvironment>(env);
            services.AddSingleton<IHostEnvironment>(env);
            services.AddLogging();
            var diagnosticListener = new System.Diagnostics.DiagnosticListener("StatementRender");
            services.AddSingleton<System.Diagnostics.DiagnosticSource>(diagnosticListener);
            services.AddSingleton(diagnosticListener);
            services.AddSingleton<Microsoft.AspNetCore.Mvc.Routing.IUrlHelperFactory, PreviewUrlHelperFactory>();
            services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
                options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
            var mvc = services.AddControllersWithViews();
            var assembly = typeof(StatementViewModel).Assembly;
            mvc.ConfigureApplicationPartManager(parts =>
                parts.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly)));

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var httpContext = new DefaultHttpContext { RequestServices = sp };
            httpContext.Request.Path = "/Vendor/Statement";
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");

            var routeData = new RouteData();
            routeData.Values["controller"] = "Vendor";
            routeData.Values["action"] = "Statement";
            var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            var engine = sp.GetRequiredService<IRazorViewEngine>();
            var viewResult = engine.GetView(null, "~/Views/Shared/_StatementTable.cshtml", isMainPage: false);
            if (!viewResult.Success)
            {
                throw new InvalidOperationException(
                    $"Could not find statement view. Searched: {string.Join(" | ", viewResult.SearchedLocations ?? Array.Empty<string>())}");
            }

            await using var writer = new StringWriter();
            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewData,
                new TempDataDictionary(httpContext, new NullTempDataProvider()),
                writer,
                new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return writer.ToString();
        }

        public static string WrapStandalone(string rendered)
        {
            var css = File.ReadAllText(Path.Combine(ContentRoot, "wwwroot", "css", "statement.css"));
            var js = File.ReadAllText(Path.Combine(ContentRoot, "wwwroot", "js", "statement.js"));
            var bootstrap = new Uri(Path.Combine(ContentRoot, "wwwroot", "lib", "bootstrap", "dist", "css", "bootstrap.rtl.min.css")).AbsoluteUri;

            var body = rendered
                .Replace("<link rel=\"stylesheet\" href=\"/css/statement.css", "<link data-remove rel=\"stylesheet\" href=\"/css/statement.css")
                .Replace("<script src=\"/js/statement.js", "<script data-remove src=\"/js/statement.js");

            return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<title>معاينة كشف الحساب</title>
<link rel=""stylesheet"" href=""{bootstrap}"" />
<style>
body {{ margin: 0; padding: 24px; background: #eef2f7; font-family: 'IBM Plex Sans Arabic', Tahoma, sans-serif; }}
.content-area {{ max-width: 1160px; margin: 0 auto; }}
@media print {{ body {{ padding: 0; background: #fff; }} }}
</style>
<style>{css}</style>
</head>
<body>
<div class=""content-area"">
{body}
</div>
<script>{js}</script>
</body>
</html>";
        }

        private sealed class RenderEnvironment : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "Accountant_sys";
            public IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = string.Empty;
            public string EnvironmentName { get; set; } = "Development";
            public IFileProvider WebRootFileProvider { get; set; } = null!;
            public string WebRootPath { get; set; } = string.Empty;
        }

        private sealed class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        /// <summary>Links are inert in the offline preview; the sheet markup is what matters.</summary>
        private sealed class PreviewUrlHelperFactory : Microsoft.AspNetCore.Mvc.Routing.IUrlHelperFactory
        {
            public IUrlHelper GetUrlHelper(ActionContext context) => new PreviewUrlHelper(context);
        }

        private sealed class PreviewUrlHelper : IUrlHelper
        {
            public PreviewUrlHelper(ActionContext actionContext) => ActionContext = actionContext;

            public ActionContext ActionContext { get; }

            public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => "#";

            public string? Content(string? contentPath) => contentPath;

            public bool IsLocalUrl(string? url) => true;

            public string? Link(string? routeName, object? values) => "#";

            public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => "#";
        }
    }
}
