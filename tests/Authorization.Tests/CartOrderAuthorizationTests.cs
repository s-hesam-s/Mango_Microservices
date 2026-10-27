using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mango.MessageBus;
using Mango.Services.OrderAPI.Controllers;
using Mango.Services.OrderAPI.Models;
using Mango.Services.ShoppingCartAPI.Controllers;
using Mango.Services.ShoppingCartAPI.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Stripe;
using Xunit;
using CartDb = Mango.Services.ShoppingCartAPI.Data.AppDbContext;
using OrderDb = Mango.Services.OrderAPI.Data.AppDbContext;
using CartProductService = Mango.Services.ShoppingCartAPI.Service.IService.IProductService;
using OrderProductService = Mango.Services.OrderAPI.Service.IService.IProductService;
using CouponService = Mango.Services.ShoppingCartAPI.Service.IService.ICouponService;
using CartProduct = Mango.Services.ShoppingCartAPI.Models.Dto.ProductDto;

namespace Authorization.Tests;

[CollectionDefinition("Isolated payment client", DisableParallelization = true)]
public class PaymentClientCollection { }

[Collection("Isolated payment client")]
public class CartOrderAuthorizationTests : IDisposable
{
    private readonly CartDb _cart = new(new DbContextOptionsBuilder<CartDb>()
        .UseInMemoryDatabase("cart-" + Guid.NewGuid()).Options);
    private readonly OrderDb _orders = new(new DbContextOptionsBuilder<OrderDb>()
        .UseInMemoryDatabase("orders-" + Guid.NewGuid()).Options);
    private readonly Mock<IMessageBus> _bus = new(MockBehavior.Strict);
    private readonly Mock<IStripeClient> _stripe = new(MockBehavior.Strict);
    private readonly IStripeClient _previousStripe;
    private readonly TestServer _server;
    private readonly HttpClient _client;

    public CartOrderAuthorizationTests()
    {
        // No application Program, SQL initializer or external integration is started.
        _previousStripe = StripeConfiguration.StripeClient;
        StripeConfiguration.StripeClient = _stripe.Object;
        _cart.CartHeaders.AddRange(new CartHeader { CartHeaderId = 10, UserId = "alice" },
            new CartHeader { CartHeaderId = 20, UserId = "bob" });
        _cart.CartDetails.AddRange(new CartDetails { CartDetailsId = 11, CartHeaderId = 10, ProductId = 1, Count = 1 },
            new CartDetails { CartDetailsId = 21, CartHeaderId = 20, ProductId = 1, Count = 2 });
        _cart.SaveChanges();
        _orders.OrderHeaders.AddRange(order(100, "alice"), order(200, "bob"));
        _orders.SaveChanges();
        _bus.Setup(b => b.PublishMessage(It.IsAny<object>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _server = createServer();
        _client = _server.CreateClient();
    }

    public static IEnumerable<object[]> ProtectedRoutes => new[]
    {
        new object[] { "GET", "/api/cart/GetCart/alice", "none" },
        new object[] { "POST", "/api/cart/ApplyCoupon", "cart" },
        new object[] { "POST", "/api/cart/CartUpsert", "cart" },
        new object[] { "POST", "/api/cart/RemoveCart", "detail" },
        new object[] { "POST", "/api/cart/EmailCartRequest", "cart" },
        new object[] { "GET", "/api/order/GetOrders", "none" },
        new object[] { "GET", "/api/order/GetOrder/100", "none" },
        new object[] { "POST", "/api/order/CreateOrder", "cart" },
        new object[] { "POST", "/api/order/CreateStripeSession", "stripe" },
        new object[] { "POST", "/api/order/ValidateStripeSession", "order" },
        new object[] { "POST", "/api/order/UpdateOrderStatus/100", "status" }
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task AnonymousRequestsAreRejectedBeforeAnyEffects(string method, string path, string body)
    {
        using var response = await send(method, path, payload(body), user: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        assertUnchanged();
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task AuthenticatedIdentityWithoutSubjectIsRejected(string method, string path, string body)
    {
        using var response = await send(method, path, payload(body), user: "", role: "ADMIN");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        assertUnchanged();
    }

    [Theory]
    [InlineData("GET", "/api/cart/GetCart/bob", "none")]
    [InlineData("POST", "/api/cart/ApplyCoupon", "cart")]
    [InlineData("POST", "/api/cart/CartUpsert", "cart")]
    [InlineData("POST", "/api/cart/RemoveCart", "detail")]
    [InlineData("POST", "/api/cart/EmailCartRequest", "cart")]
    [InlineData("GET", "/api/order/GetOrders?userId=bob", "none")]
    [InlineData("GET", "/api/order/GetOrder/200", "none")]
    [InlineData("POST", "/api/order/CreateOrder", "cart")]
    [InlineData("POST", "/api/order/CreateStripeSession", "stripe")]
    [InlineData("POST", "/api/order/ValidateStripeSession", "order")]
    [InlineData("POST", "/api/order/UpdateOrderStatus/200", "status")]
    public async Task AnotherCustomersDataCannotBeReadOrChanged(string method, string path, string body)
    {
        using var response = await send(method, path, payload(body, foreign: true));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        assertUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerCanReadCartAndOrdersWithMappedOrRawSubject(bool rawSubject)
    {
        foreach (var path in new[] { "/api/cart/GetCart/alice", "/api/order/GetOrder/100", "/api/order/GetOrders" })
        {
            using var response = await send("GET", path, rawSubject: rawSubject);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("isSuccess").GetBoolean());
            var result = json.RootElement.GetProperty("result");
            if (result.ValueKind == JsonValueKind.Array)
            {
                Assert.Single(result.EnumerateArray());
                Assert.Equal("alice", result[0].GetProperty("userId").GetString());
            }
            else if (path.Contains("GetOrder/")) Assert.Equal("alice", result.GetProperty("userId").GetString());
            else Assert.Equal("alice", result.GetProperty("cartHeader").GetProperty("userId").GetString());
        }
    }

    [Fact]
    public async Task AdministratorCanReadOrdersAndChangeOperationalStatus()
    {
        using var list = await send("GET", "/api/order/GetOrders", role: "ADMIN");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("result").GetArrayLength());
        using var detail = await send("GET", "/api/order/GetOrder/200", role: "ADMIN");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var update = await send("POST", "/api/order/UpdateOrderStatus/200", "ReadyForPickup", role: "ADMIN");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("ReadyForPickup", _orders.OrderHeaders.Find(200).Status);
        Assert.Equal("Approved", _orders.OrderHeaders.Find(100).Status);
    }

    [Theory]
    [InlineData("ReadyForPickup")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task CustomerCannotChangeEvenTheirOwnOrderStatus(string status)
    {
        using var response = await send("POST", "/api/order/UpdateOrderStatus/100", status);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        assertUnchanged();
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Refunded")]
    [InlineData("unknown")]
    public async Task AdministratorCannotAssignPaymentOrUnknownStatuses(string status)
    {
        using var response = await send("POST", "/api/order/UpdateOrderStatus/100", status, role: "ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        assertUnchanged();
    }

    [Theory]
    [InlineData("GET", "/api/cart/GetCart/bob", "none")]
    [InlineData("POST", "/api/order/CreateStripeSession", "stripe")]
    [InlineData("POST", "/api/order/ValidateStripeSession", "order")]
    public async Task AdministratorDoesNotImpersonateCartOrPaymentOwner(string method, string path, string body)
    {
        using var response = await send(method, path, payload(body, foreign: true), role: "ADMIN");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        assertUnchanged();
    }

    [Fact]
    public async Task CartUpsertIgnoresForeignRecordKeysAndUsesAuthenticatedOwner()
    {
        using var response = await send("POST", "/api/cart/CartUpsert", cartPayload(null, 20, 21, productId: 2));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bob", _cart.CartHeaders.Find(20).UserId);
        Assert.Equal(2, _cart.CartDetails.Find(21).Count);
        Assert.Contains(_cart.CartDetails, d => d.CartHeaderId == 10 && d.ProductId == 2 && d.CartDetailsId != 21);
    }

    [Fact]
    public async Task NestedCartNavigationCannotAttachAnItemToAnotherOwner()
    {
        using var response = await send("POST", "/api/cart/CartUpsert", cartPayload(null, 10, 0, 2, nestedForeign: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_cart.CartDetails, d => d.ProductId == 2 && d.CartHeaderId == 10);
        Assert.DoesNotContain(_cart.CartDetails, d => d.ProductId == 2 && d.CartHeaderId == 20);
        Assert.Equal(2, _cart.CartDetails.Find(21).Count);
    }

    [Fact]
    public async Task NewCartCannotChooseAnotherCustomersRecordKeys()
    {
        using var response = await send("POST", "/api/cart/CartUpsert", cartPayload(null, 20, 21), user: "charlie");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = _cart.CartHeaders.Single(h => h.UserId == "charlie");
        Assert.NotEqual(20, created.CartHeaderId);
        Assert.Contains(_cart.CartDetails, d => d.CartHeaderId == created.CartHeaderId && d.CartDetailsId != 21);
        Assert.Equal(2, _cart.CartDetails.Find(21).Count);
    }

    [Theory]
    [InlineData("ApplyCoupon")]
    [InlineData("EmailCartRequest")]
    public async Task ForeignHeaderKeyIsRejectedEvenWhenPayloadUserMatches(string action)
    {
        using var response = await send("POST", "/api/cart/" + action, cartPayload("alice", 20));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        assertUnchanged();
    }

    [Fact]
    public async Task OwnerCanApplyCouponEmailCartAndRemoveOwnItem()
    {
        using var coupon = await send("POST", "/api/cart/ApplyCoupon", cartPayload("alice", 10));
        Assert.Equal(HttpStatusCode.OK, coupon.StatusCode);
        Assert.Equal("TEST", _cart.CartHeaders.Find(10).CouponCode);
        Assert.Null(_cart.CartHeaders.Find(20).CouponCode);
        using var email = await send("POST", "/api/cart/EmailCartRequest", cartPayload(null, 10));
        Assert.Equal(HttpStatusCode.OK, email.StatusCode);
        _bus.Verify(b => b.PublishMessage(It.Is<Mango.Services.ShoppingCartAPI.Models.Dto.CartDto>(
            d => d.CartHeader.UserId == "alice" && d.CartHeader.CartHeaderId == 10), "email-cart-test"), Times.Once);
        using var remove = await send("POST", "/api/cart/RemoveCart", 11);
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Null(_cart.CartDetails.Find(11));
        Assert.NotNull(_cart.CartDetails.Find(21));
    }

    [Fact]
    public async Task CreateOrderBindsMissingOwnerToSubject()
    {
        using var response = await send("POST", "/api/order/CreateOrder", cartPayload(null, 10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = _orders.OrderHeaders.Single(o => o.OrderHeaderId != 100 && o.OrderHeaderId != 200);
        Assert.Equal("alice", created.UserId);
        Assert.Equal("Pending", created.Status);
    }

    [Theory]
    [InlineData("GET", "/api/order/GetOrder/999", "none")]
    [InlineData("POST", "/api/order/ValidateStripeSession", "missingOrder")]
    public async Task MissingResourcesReturnNotFoundWithoutExternalCalls(string method, string path, string body)
    {
        using var response = await send(method, path, payload(body));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        assertUnchanged();
    }

    private static OrderHeader order(int id, string owner) => new()
    {
        OrderHeaderId = id, UserId = owner, Status = "Approved", OrderTotal = 12,
        OrderDetails = new[] { new OrderDetails { ProductId = 1, ProductName = "Fixture", Price = 12, Count = 1 } }
    };

    private static object cartPayload(string owner, int headerId, int detailId = 11, int productId = 1, bool nestedForeign = false) => new
    {
        cartHeader = new { userId = owner, cartHeaderId = headerId, couponCode = "TEST", cartTotal = 12 },
        cartDetails = new[] { new { cartDetailsId = detailId, productId, count = 1,
            cartHeader = nestedForeign ? new { cartHeaderId = 20, userId = "bob" } : null, product = new
        { productId, name = "Fixture", price = 12, description = "Fixture", categoryName = "Test", imageUrl = "fixture.png" } } }
    };

    private static object payload(string kind, bool foreign = false) => kind switch
    {
        "cart" => cartPayload(foreign ? "bob" : "alice", foreign ? 20 : 10),
        "detail" => foreign ? 21 : 11,
        "order" => foreign ? 200 : 100,
        "missingOrder" => 999,
        "status" => "Cancelled",
        "stripe" => new { approvedUrl = "https://example.invalid/success", cancelUrl = "https://example.invalid/cancel",
            orderHeader = new { orderHeaderId = foreign ? 200 : 100, userId = foreign ? "bob" : "alice", orderDetails = Array.Empty<object>() } },
        _ => null
    };

    private async Task<HttpResponseMessage> send(string method, string path, object body = null,
        string user = "alice", string role = "CUSTOMER", bool rawSubject = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (user != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{role}|{user}|{(rawSubject ? "sub" : "mapped")}");
        if (body != null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private TestServer createServer()
    {
        var products = new Mock<CartProductService>();
        products.Setup(p => p.GetProducts()).ReturnsAsync(new[] { new CartProduct { ProductId = 1, Name = "Fixture", Price = 12 } });
        var orderProducts = new Mock<OrderProductService>();
        orderProducts.Setup(p => p.GetProducts()).ReturnsAsync(new[] {
            new Mango.Services.OrderAPI.Models.Dto.ProductDto { ProductId = 1, Name = "Fixture", Price = 12 } });
        var orderCoupons = new Mock<Mango.Services.OrderAPI.Service.IService.ICouponService>();
        orderCoupons.Setup(c => c.GetCoupon("TEST")).ReturnsAsync(
            new Mango.Services.OrderAPI.Models.Dto.CouponDto { CouponCode = "TEST", DiscountAmount = 0 });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            { ["TopicAndQueueNames:EmailShoppingCartQueue"] = "email-cart-test" }).Build();
        return new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(CartAPIController).Assembly)
                .AddApplicationPart(typeof(OrderAPIController).Assembly).AddControllersAsServices();
            services.AddTransient(_ => new CartAPIController(_cart,
                Mango.Services.ShoppingCartAPI.MappingConfig.RegisterMaps().CreateMapper(), products.Object,
                new Mock<CouponService>().Object, _bus.Object, config));
            services.AddTransient(_ => new OrderAPIController(_orders, orderProducts.Object,
                Mango.Services.OrderAPI.MappingConfig.RegisterMaps().CreateMapper(), config, _bus.Object,
                orderCoupons.Object));
            services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>("Bearer", _ => { });
            services.AddAuthorization();
        }).Configure(app =>
        {
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
    }

    private void assertUnchanged()
    {
        Assert.Equal(2, _cart.CartHeaders.Count());
        Assert.Equal(2, _cart.CartDetails.Count());
        Assert.Equal(2, _cart.CartDetails.Find(21).Count);
        Assert.All(_cart.CartHeaders, h => Assert.Null(h.CouponCode));
        Assert.Equal(2, _orders.OrderHeaders.Count());
        Assert.All(_orders.OrderHeaders, o => Assert.Equal("Approved", o.Status));
        _bus.Verify(b => b.PublishMessage(It.IsAny<object>(), It.IsAny<string>()), Times.Never);
        _stripe.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        try
        {
            _stripe.VerifyNoOtherCalls();
        }
        finally
        {
            _client.Dispose(); _server.Dispose(); _cart.Dispose(); _orders.Dispose();
            StripeConfiguration.StripeClient = _previousStripe;
        }
    }

    private class TestIdentityHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
            var parts = header[7..].Split('|');
            var claims = new List<Claim> { new(ClaimTypes.Role, parts[0]) };
            if (!string.IsNullOrEmpty(parts[1])) claims.Add(new Claim(parts[2] == "sub" ? "sub" : ClaimTypes.NameIdentifier, parts[1]));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
