using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mango.Services.ProductAPI;
using Mango.Services.ProductAPI.Controllers;
using Mango.Services.ProductAPI.Models;
using Mango.Services.ProductAPI.Models.Dto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ProductDb = Mango.Services.ProductAPI.Data.AppDbContext;

namespace Authorization.Tests;

public class ProductSecurityTests : IDisposable
{
    private readonly ProductDb db = new(new DbContextOptionsBuilder<ProductDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public ProductSecurityTests()
    {
        db.Products.Add(new Product { ProductId = 1, Name = "Stored", Price = 20, Description = "Description", CategoryName = "Category",
            ImageUrl = "https://example.invalid/image.png", ImageLocalPath = "wwwroot/ProductImages/stored.png" });
        db.SaveChanges(); db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("CUSTOMER", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    public async Task ProductCreationRequiresAdministratorAndBindsMultipartForm(string role, HttpStatusCode expected)
    {
        using var server = createServer(); using var client = server.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Add("Authorization", "Bearer " + role);
        using var form = formData();
        var response = await client.PostAsync("/api/product", form);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(role == "ADMIN" ? 2 : 1, db.Products.Count());
        Assert.DoesNotContain(db.Products, p => p.ProductId == 999);
    }

    [Fact]
    public async Task InvalidImageCannotCreateAPartialProduct()
    {
        using var server = createServer(); using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer ADMIN");
        using var form = formData(); form.Add(new ByteArrayContent("<script>sample</script>"u8.ToArray()), "Image", "image.svg");
        var response = await client.PostAsync("/api/product", form);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Single(db.Products);
    }

    [Fact]
    public async Task InvalidPriceIsRejectedBeforeControllerWrites()
    {
        using var server = createServer(); using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer ADMIN");
        using var form = formData("-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/product", form)).StatusCode);
        Assert.Single(db.Products);
    }

    [Fact]
    public void EditingWithoutUploadIgnoresThePostedStoragePath()
    {
        var controller = new ProductAPIController(db, new MappingConfig())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var response = controller.Put(new ProductDto { ProductId = 1, Name = "Changed", Price = 25,
            Description = "Text", CategoryName = "Category", ImageLocalPath = "../../outside.png", ImageUrl = "https://other.example.invalid/" });
        Assert.True(response.IsSuccess);
        var stored = db.Products.AsNoTracking().Single();
        Assert.Equal("Changed", stored.Name);
        Assert.Equal("wwwroot/ProductImages/stored.png", stored.ImageLocalPath);
        Assert.Equal("https://example.invalid/image.png", stored.ImageUrl);
    }

    private TestServer createServer() => new(new WebHostBuilder().ConfigureServices(services =>
    {
        services.AddSingleton(db);
        services.AddSingleton(new MappingConfig());
        services.AddControllers().AddApplicationPart(typeof(ProductAPIController).Assembly);
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, ProductBearerHandler>("Test", _ => { });
        services.AddAuthorization();
    }).Configure(app =>
    {
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }));

    private static MultipartFormDataContent formData(string price = "25")
    {
        var form = new MultipartFormDataContent();
        foreach (var pair in new Dictionary<string, string> { ["Name"] = "Created", ["Price"] = price,
            ["Description"] = "Text", ["CategoryName"] = "Category", ["ProductId"] = "999", ["ImageLocalPath"] = "../../outside.png" })
            form.Add(new StringContent(pair.Value), pair.Key);
        return form;
    }

    public void Dispose() => db.Dispose();
}

public class ProductBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ProductBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ")) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        { new Claim(ClaimTypes.NameIdentifier, "fixture"), new Claim(ClaimTypes.Role, header[7..]) }, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}
