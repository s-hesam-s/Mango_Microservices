using System.Reflection;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (repoRoot is not null && !Directory.Exists(Path.Combine(repoRoot.FullName, "Mango.Web")))
    repoRoot = repoRoot.Parent;
if (repoRoot is null) throw new InvalidOperationException("Run this preview inside its repository checkout.");
var rootPath = repoRoot.FullName;
var webRoot = Path.Combine(rootPath, "Mango.Web", "wwwroot");
var webAssembly = typeof(Mango.Web.Controllers.HomeController).Assembly;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
    ApplicationName = webAssembly.GetName().Name,
    EnvironmentName = "Production",
    ContentRootPath = Path.Combine(rootPath, "tools", "Preview"),
    WebRootPath = webRoot
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.WebHost.UseUrls(args.Contains("--capture") ? "http://127.0.0.1:8822" : "http://127.0.0.1:8812");
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews().AddApplicationPart(webAssembly)
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new PreviewControllers()));

var app = builder.Build();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(rootPath, "Mango.Services.ProductAPI", "wwwroot", "ProductImages")),
    RequestPath = "/fixture-products"
});

app.Use(async (context, next) => {
    if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
    var original = context.Response.Body;
    using var buffer = new MemoryStream();
    context.Response.Body = buffer;
    try {
        await next();
        buffer.Position = 0;
        if (context.Response.ContentType?.StartsWith("text/html") == true) {
            var html = await new StreamReader(buffer).ReadToEndAsync();
            const string banner = "<aside style='padding:14px 24px;background:#173641;color:#fff;font:15px system-ui;text-align:center'>READ-ONLY FIXTURE PREVIEW · Sample catalog and details · No database, payments or external APIs. Forms and other navigation are inactive.</aside>";
            html = html.Replace("<body>", "<body>" + banner);
            context.Response.Body = original;
            await context.Response.WriteAsync(html);
        } else {
            context.Response.Body = original;
            await buffer.CopyToAsync(original);
        }
    } finally { context.Response.Body = original; }
});
app.MapGet("/preview-info", () => new { mode="read-only fixture", view="production Razor", database=false, externalApis=false });
app.MapControllerRoute("preview", "{controller=Preview}/{action=Index}");
app.Run();

public sealed class PreviewController : Controller
{
    [HttpGet]
    public IActionResult Index() => View("~/Views/Home/Index.cshtml", fixtureProducts());

    [HttpGet]
    public IActionResult ProductDetails(int productId)
    {
        var product = fixtureProducts().SingleOrDefault(p => p.ProductId == productId);
        return product is null ? NotFound() : View("~/Views/Home/ProductDetails.cshtml", product);
    }

    private static Mango.Web.Models.ProductDto[] fixtureProducts() => new[] {
            new Mango.Web.Models.ProductDto { ProductId=1, Name="Samosa", Description="Sample starter from the fixture catalog.", Price=5, CategoryName="Starter", ImageUrl="/fixture-products/1.jpg" },
            new Mango.Web.Models.ProductDto { ProductId=2, Name="Paneer Tikka", Description="Sample main dish from the fixture catalog.", Price=12.5, CategoryName="Main", ImageUrl="/fixture-products/4.jpg" },
            new Mango.Web.Models.ProductDto { ProductId=3, Name="Dessert", Description="Sample dessert from the fixture catalog.", Price=7.5, CategoryName="Dessert", ImageUrl="/fixture-products/5.jpg" }
        };
}

public sealed class PreviewControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        feature.Controllers.Clear();
        feature.Controllers.Add(typeof(PreviewController).GetTypeInfo());
    }
}
