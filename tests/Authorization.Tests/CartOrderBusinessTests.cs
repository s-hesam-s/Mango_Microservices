using System.Net.Http;
using System.Security.Claims;
using Mango.MessageBus;
using Mango.Services.OrderAPI.Controllers;
using Mango.Services.OrderAPI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Stripe;
using Stripe.Checkout;
using Xunit;
using CartDb = Mango.Services.ShoppingCartAPI.Data.AppDbContext;
using OrderDb = Mango.Services.OrderAPI.Data.AppDbContext;
using CartController = Mango.Services.ShoppingCartAPI.Controllers.CartAPIController;
using CartModels = Mango.Services.ShoppingCartAPI.Models;
using CartDtos = Mango.Services.ShoppingCartAPI.Models.Dto;
using OrderDtos = Mango.Services.OrderAPI.Models.Dto;
using CartProducts = Mango.Services.ShoppingCartAPI.Service.IService.IProductService;
using OrderProducts = Mango.Services.OrderAPI.Service.IService.IProductService;
using CartCoupons = Mango.Services.ShoppingCartAPI.Service.IService.ICouponService;
using OrderCoupons = Mango.Services.OrderAPI.Service.IService.ICouponService;

namespace Authorization.Tests;

[Collection("Isolated payment client")]
public class CartOrderBusinessTests : IDisposable
{
    private readonly CartDb _cart = new(new DbContextOptionsBuilder<CartDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly OrderDb _db = new(new DbContextOptionsBuilder<OrderDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<CartProducts> _cartProducts = new();
    private readonly Mock<OrderProducts> _products = new();
    private readonly Mock<CartCoupons> _cartCoupons = new();
    private readonly Mock<OrderCoupons> _coupons = new();
    private readonly Mock<IMessageBus> _bus = new(MockBehavior.Strict);
    private readonly Mock<IStripeClient> _stripe = new(MockBehavior.Strict);
    private readonly IStripeClient _previousStripe;
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string> { ["TopicAndQueueNames:OrderCreatedTopic"] = "test-rewards" }).Build();

    public CartOrderBusinessTests()
    {
        _previousStripe = StripeConfiguration.StripeClient;
        StripeConfiguration.StripeClient = _stripe.Object;
        _cart.CartHeaders.Add(new CartModels.CartHeader { CartHeaderId = 10, UserId = "alice", CouponCode = "SAVE" });
        _cart.CartDetails.Add(new CartModels.CartDetails { CartDetailsId = 11, CartHeaderId = 10, ProductId = 1, Count = 2 });
        _cart.SaveChanges(); _cart.ChangeTracker.Clear();
        _db.OrderHeaders.Add(new OrderHeader { OrderHeaderId = 100, UserId = "alice", Status = "Pending",
            OrderTotal = 34.98, Discount = 5, CouponCode = "SAVE", StripeSessionId = "cs_test",
            OrderDetails = new[] { new OrderDetails { ProductId = 1, ProductName = "Catalog item", Price = 19.99, Count = 2 } } });
        _db.SaveChanges();
        _products.Setup(s => s.GetProducts()).ReturnsAsync(new[] { new OrderDtos.ProductDto { ProductId = 1, Name = "Catalog item", Price = 19.99 } });
        _cartProducts.Setup(s => s.GetProducts()).ReturnsAsync(new[] { new CartDtos.ProductDto { ProductId = 1, Name = "Catalog item", Price = 19.99 } });
        _bus.Setup(b => b.PublishMessage(It.IsAny<object>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)]
    public async Task CartRejectsNonPositiveQuantitiesWithoutWriting(int count)
    {
        var input = cartInput(count);
        Assert.Equal(400, status(await cart().CartUpsert(input)));
        Assert.Equal(2, _cart.CartDetails.AsNoTracking().Single().Count);
    }

    [Fact]
    public async Task CartRejectsQuantityOverflow()
    {
        Assert.Equal(400, status(await cart().CartUpsert(cartInput(int.MaxValue))));
        Assert.Equal(2, _cart.CartDetails.AsNoTracking().Single().Count);
    }

    [Fact]
    public async Task CartAddsQuantityForExistingProduct()
    {
        var result = await cart().CartUpsert(cartInput(3));
        Assert.True(result.Value.IsSuccess);
        Assert.Equal(5, _cart.CartDetails.AsNoTracking().Single().Count);
    }

    [Fact]
    public async Task CartRejectsMultipleItemsInsteadOfSilentlyIgnoringThem()
    {
        var input = cartInput(1); input.CartDetails = input.CartDetails.Concat(input.CartDetails).ToArray();
        Assert.Equal(400, status(await cart().CartUpsert(input)));
        Assert.Equal(2, _cart.CartDetails.AsNoTracking().Single().Count);
    }

    [Theory]
    [InlineData(39, 5, 34.98)] [InlineData(40, 5, 39.98)] [InlineData(0, 100, 0)]
    public async Task CartUsesCatalogPricesAndCapsEligibleDiscount(int minimum, double discount, double expected)
    {
        _cartCoupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new CartDtos.CouponDto { CouponCode = "SAVE", MinAmount = minimum, DiscountAmount = discount });
        var result = await cart().GetCart("alice");
        var data = Assert.IsType<CartDtos.CartDto>(result.Value.Result);
        Assert.Equal(expected, data.CartHeader.CartTotal, 2);
        Assert.Equal(39.98 - expected, data.CartHeader.Discount, 2);
    }

    [Fact]
    public async Task CouponIsEligibleAtExactMinimumInCartAndOrder()
    {
        _cartProducts.Setup(s => s.GetProducts()).ReturnsAsync(new[] { new CartDtos.ProductDto { ProductId = 1, Name = "Item", Price = 20 } });
        _products.Setup(s => s.GetProducts()).ReturnsAsync(new[] { new OrderDtos.ProductDto { ProductId = 1, Name = "Item", Price = 20 } });
        _cartCoupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new CartDtos.CouponDto { CouponCode = "SAVE", MinAmount = 40, DiscountAmount = 5 });
        _coupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new OrderDtos.CouponDto { CouponCode = "SAVE", MinAmount = 40, DiscountAmount = 5 });
        var cartResult = await cart().GetCart("alice");
        Assert.Equal(35, Assert.IsType<CartDtos.CartDto>(cartResult.Value.Result).CartHeader.CartTotal);
        var orderResult = await order().CreateOrder(orderInput());
        Assert.Equal(35, Assert.IsType<OrderDtos.OrderHeaderDto>(orderResult.Value.Result).OrderTotal);
    }

    [Theory]
    [InlineData("product")] [InlineData("coupon")] [InlineData("missingProduct")] [InlineData("invalidCoupon")]
    public async Task CartPricingFailuresDoNotReturnSuccessfulTotals(string failure)
    {
        if (failure == "product") _cartProducts.Setup(s => s.GetProducts()).ThrowsAsync(new HttpRequestException("fixture failure"));
        if (failure == "coupon") _cartCoupons.Setup(s => s.GetCoupon("SAVE")).ThrowsAsync(new HttpRequestException("fixture failure"));
        if (failure == "missingProduct") _cartProducts.Setup(s => s.GetProducts()).ReturnsAsync(Array.Empty<CartDtos.ProductDto>());
        if (failure == "invalidCoupon") _cartCoupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new CartDtos.CouponDto { CouponCode = "SAVE", DiscountAmount = -1 });
        var result = await cart().GetCart("alice");
        Assert.Equal(failure == "missingProduct" ? 409 : 503, status(result));
        Assert.Equal(2, _cart.CartDetails.AsNoTracking().Single().Count);
    }

    [Fact]
    public async Task OrderIgnoresForgedPricesTotalsDiscountsAndRecordKeys()
    {
        _coupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new OrderDtos.CouponDto { CouponCode = "SAVE", MinAmount = 0, DiscountAmount = 5 });
        var result = await order().CreateOrder(orderInput());
        var created = Assert.IsType<OrderDtos.OrderHeaderDto>(result.Value.Result);
        Assert.Equal(34.98, created.OrderTotal, 2); Assert.Equal(5, created.Discount);
        Assert.Equal("Pending", created.Status); Assert.Equal("alice", created.UserId);
        Assert.Equal(19.99, created.OrderDetails.Single().Price, 2);
        Assert.Equal("Catalog item", created.OrderDetails.Single().ProductName);
        Assert.NotEqual(100, created.OrderHeaderId);
        Assert.Equal(2, _db.OrderHeaders.Count());
    }

    [Theory]
    [InlineData("empty")] [InlineData("zero")] [InlineData("negative")] [InlineData("duplicate")] [InlineData("unknown")] [InlineData("coupon")]
    public async Task InvalidOrdersHaveNoDatabaseOrPaymentEffects(string invalid)
    {
        var input = orderInput(); input.CartHeader.CouponCode = null;
        if (invalid == "empty") input.CartDetails = Array.Empty<OrderDtos.CartDetailsDto>();
        if (invalid == "zero") input.CartDetails.First().Count = 0;
        if (invalid == "negative") input.CartDetails.First().Count = -1;
        if (invalid == "duplicate") input.CartDetails = input.CartDetails.Concat(input.CartDetails).ToArray();
        if (invalid == "unknown") input.CartDetails.First().ProductId = 999;
        if (invalid == "coupon") input.CartHeader.CouponCode = "INVALID";
        Assert.Equal(400, status(await order().CreateOrder(input)));
        Assert.Single(_db.OrderHeaders); Assert.Empty(_bus.Invocations);
    }

    [Theory]
    [InlineData(0, 100, 0)] [InlineData(40, 5, 39.98)]
    public async Task OrderDiscountCannotMakeTotalNegativeOrApplyBelowMinimum(int minimum, double discount, double expected)
    {
        _coupons.Setup(s => s.GetCoupon("SAVE")).ReturnsAsync(new OrderDtos.CouponDto { CouponCode = "SAVE", MinAmount = minimum, DiscountAmount = discount });
        var result = await order().CreateOrder(orderInput());
        Assert.Equal(expected, Assert.IsType<OrderDtos.OrderHeaderDto>(result.Value.Result).OrderTotal, 2);
    }

    [Theory]
    [InlineData("product")] [InlineData("coupon")] [InlineData("invalidPrice")]
    public async Task OrderDependencyFailuresDoNotPersistAnOrder(string source)
    {
        if (source == "product") _products.Setup(s => s.GetProducts()).ThrowsAsync(new HttpRequestException("fixture failure"));
        if (source == "coupon") _coupons.Setup(s => s.GetCoupon("SAVE")).ThrowsAsync(new HttpRequestException("fixture failure"));
        if (source == "invalidPrice") _products.Setup(s => s.GetProducts()).ReturnsAsync(new[] { new OrderDtos.ProductDto { ProductId = 1, Price = double.NaN } });
        Assert.Equal(503, status(await order().CreateOrder(orderInput()))); Assert.Single(_db.OrderHeaders);
    }

    [Theory]
    [InlineData("Pending", "ReadyForPickup")] [InlineData("Pending", "Completed")] [InlineData("Pending", "Cancelled")]
    [InlineData("Completed", "ReadyForPickup")] [InlineData("Completed", "Cancelled")] [InlineData("Cancelled", "Completed")]
    [InlineData("Refunded", "ReadyForPickup")] [InlineData("Approved", "Completed")]
    public async Task InvalidTransitionsDoNotWriteOrRefund(string current, string target)
    {
        _db.OrderHeaders.Find(100).Status = current; await _db.SaveChangesAsync();
        Assert.Equal(409, status(await order().UpdateOrderStatus(100, target)));
        Assert.Equal(current, _db.OrderHeaders.AsNoTracking().Single().Status);
    }

    [Theory]
    [InlineData("Approved", "ReadyForPickup")] [InlineData("ReadyForPickup", "Completed")]
    [InlineData("Completed", "Completed")] [InlineData("Cancelled", "Cancelled")]
    public async Task ValidAndRepeatedTransitionsHaveNoPaymentSideEffects(string current, string target)
    {
        _db.OrderHeaders.Find(100).Status = current; await _db.SaveChangesAsync();
        Assert.True((await order().UpdateOrderStatus(100, target)).Value.IsSuccess);
        Assert.Equal(target, _db.OrderHeaders.AsNoTracking().Single().Status);
    }

    [Theory]
    [InlineData("Approved", "succeeded")] [InlineData("ReadyForPickup", "pending")]
    public async Task CancellationUsesStoredPaymentAndStableRefundKeyOnce(string current, string refundStatus)
    {
        var stored = _db.OrderHeaders.Find(100); stored.Status = current; stored.PaymentIntentId = "pi_test"; await _db.SaveChangesAsync();
        _stripe.Setup(s => s.RequestAsync<Refund>(HttpMethod.Post, "/v1/refunds",
            It.Is<BaseOptions>(o => ((RefundCreateOptions)o).PaymentIntent == "pi_test"),
            It.Is<RequestOptions>(o => o.IdempotencyKey == "order-100-refund-v1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Refund { Id = "re_test", Status = refundStatus });
        Assert.True((await order().UpdateOrderStatus(100, "Cancelled")).Value.IsSuccess);
        Assert.True((await order().UpdateOrderStatus(100, "Cancelled")).Value.IsSuccess);
        _stripe.Verify(s => s.RequestAsync<Refund>(HttpMethod.Post, "/v1/refunds", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Cancelled", _db.OrderHeaders.AsNoTracking().Single().Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("failed")] [InlineData("exception")]
    public async Task FailedRefundDoesNotCancelOrder(string failure)
    {
        var stored = _db.OrderHeaders.Find(100); stored.Status = "Approved"; stored.PaymentIntentId = failure == "missing" ? null : "pi_test"; await _db.SaveChangesAsync();
        if (failure == "failed") _stripe.Setup(s => s.RequestAsync<Refund>(HttpMethod.Post, "/v1/refunds", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Refund { Status = "failed" });
        if (failure == "exception") _stripe.Setup(s => s.RequestAsync<Refund>(HttpMethod.Post, "/v1/refunds", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("fixture failure"));
        Assert.Equal(failure == "exception" ? 503 : 409, status(await order().UpdateOrderStatus(100, "Cancelled")));
        Assert.Equal("Approved", _db.OrderHeaders.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task CheckoutBuildsStripeAmountFromStoredSnapshotAndUsesStableKeys()
    {
        _db.OrderHeaders.Find(100).StripeSessionId = null; await _db.SaveChangesAsync();
        _stripe.Setup(s => s.RequestAsync<Coupon>(HttpMethod.Post, "/v1/coupons", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((m, p, o, r, c) => {
                Assert.Equal(500, ((CouponCreateOptions)o).AmountOff); Assert.Equal("usd", ((CouponCreateOptions)o).Currency);
                Assert.Equal("order-100-discount-v1", r.IdempotencyKey); })
            .ReturnsAsync(new Coupon { Id = "coupon_test" });
        _stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((m, p, o, r, c) => {
                var options = (SessionCreateOptions)o; Assert.Equal("100", options.ClientReferenceId);
                Assert.Equal(1999, options.LineItems.Single().PriceData.UnitAmount); Assert.Equal(2, options.LineItems.Single().Quantity);
                Assert.Equal("Catalog item", options.LineItems.Single().PriceData.ProductData.Name);
                Assert.Equal("coupon_test", options.Discounts.Single().Coupon); Assert.Equal("order-100-checkout-v1", r.IdempotencyKey); })
            .ReturnsAsync(new Session { Id = "cs_created", Url = "https://checkout.stripe.com/test" });
        var request = stripeInput();
        var result = await order().CreateStripeSession(request);
        Assert.True(result.Value.IsSuccess); Assert.Equal(34.98, request.OrderHeader.OrderTotal, 2);
        Assert.Equal("cs_created", _db.OrderHeaders.AsNoTracking().Single().StripeSessionId);
    }

    [Theory]
    [InlineData("completed")] [InlineData("inconsistent")] [InlineData("zero")] [InlineData("url")]
    public async Task InvalidCheckoutCannotReachStripe(string invalid)
    {
        var stored = _db.OrderHeaders.Find(100); var request = stripeInput();
        if (invalid == "completed") stored.Status = "Completed";
        if (invalid == "inconsistent") stored.OrderTotal = 1;
        if (invalid == "zero") { stored.OrderTotal = 0; stored.Discount = 39.98; }
        if (invalid == "url") request.ApprovedUrl = "javascript:alert(1)";
        await _db.SaveChangesAsync();
        Assert.Equal(invalid == "url" ? 400 : 409, status(await order().CreateStripeSession(request)));
    }

    [Fact]
    public async Task StripeFailureDoesNotPersistSessionIdentifier()
    {
        var stored = _db.OrderHeaders.Find(100); stored.StripeSessionId = null;
        stored.Discount = 0; stored.OrderTotal = 39.98; await _db.SaveChangesAsync();
        _stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("fixture failure"));
        Assert.Equal(503, status(await order().CreateStripeSession(stripeInput())));
        Assert.Null(_db.OrderHeaders.AsNoTracking().Single().StripeSessionId);
    }

    [Theory]
    [InlineData("open", "unpaid", true)] [InlineData("complete", "paid", false)] [InlineData("expired", "unpaid", false)]
    public async Task ExistingCheckoutIsReusedOrRejectedWithoutCreatingAnotherPayment(string current, string paymentStatus, bool reusable)
    {
        _stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_test", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = "cs_test", Status = current, PaymentStatus = paymentStatus,
                ClientReferenceId = "100", Currency = "usd", AmountTotal = 3498, Url = "https://checkout.stripe.com/test" });
        var result = await order().CreateStripeSession(stripeInput());
        if (reusable) Assert.True(result.Value.IsSuccess);
        else Assert.Equal(409, status(result));
        Assert.Single(_stripe.Invocations);
    }

    [Fact]
    public async Task MultipleProductsUseRoundedCatalogUnitsAndDecimalSummation()
    {
        _products.Setup(s => s.GetProducts()).ReturnsAsync(new[] {
            new OrderDtos.ProductDto { ProductId = 1, Name = "First", Price = 1.005 },
            new OrderDtos.ProductDto { ProductId = 2, Name = "Second", Price = 0.10 } });
        var input = orderInput(); input.CartHeader.CouponCode = null;
        input.CartDetails = new[] { new OrderDtos.CartDetailsDto { ProductId = 1, Count = 3 }, new OrderDtos.CartDetailsDto { ProductId = 2, Count = 3 } };
        var result = await order().CreateOrder(input);
        var created = Assert.IsType<OrderDtos.OrderHeaderDto>(result.Value.Result);
        Assert.Equal(3.33, created.OrderTotal, 2);
        Assert.Equal(1.01, created.OrderDetails.First().Price, 2);
    }

    [Fact]
    public async Task ConfirmingSamePaymentTwiceApprovesAndPublishesOnce()
    {
        payment();
        Assert.True((await order().ValidateStripeSession(100)).Value.IsSuccess);
        Assert.True((await order().ValidateStripeSession(100)).Value.IsSuccess);
        Assert.Equal("Approved", _db.OrderHeaders.AsNoTracking().Single().Status);
        _bus.Verify(b => b.PublishMessage(It.Is<OrderDtos.RewardsDto>(r => r.OrderId == 100 && r.UserId == "alice" && r.RewardsActivity == 35), "test-rewards"), Times.Once);
        Assert.Equal(2, _stripe.Invocations.Count);
    }

    [Theory]
    [InlineData("Approved")] [InlineData("ReadyForPickup")] [InlineData("Completed")] [InlineData("Cancelled")] [InlineData("Refunded")]
    public async Task ConfirmationCannotReopenOrRepublishExistingOrder(string current)
    {
        _db.OrderHeaders.Find(100).Status = current; await _db.SaveChangesAsync();
        var result = await order().ValidateStripeSession(100);
        if (current == "Cancelled" || current == "Refunded") Assert.Equal(409, status(result));
        else Assert.True(result.Value.IsSuccess);
        Assert.Equal(current, _db.OrderHeaders.AsNoTracking().Single().Status); Assert.Empty(_bus.Invocations);
    }

    [Theory]
    [InlineData("reference")] [InlineData("session")] [InlineData("amount")] [InlineData("currency")] [InlineData("unpaid")]
    [InlineData("intentAmount")] [InlineData("intentCurrency")] [InlineData("intentStatus")] [InlineData("intentId")]
    public async Task PaymentMismatchCannotApproveOrPublish(string mismatch)
    {
        payment(mismatch);
        Assert.Equal(409, status(await order().ValidateStripeSession(100)));
        Assert.Equal("Pending", _db.OrderHeaders.AsNoTracking().Single().Status); Assert.Empty(_bus.Invocations);
    }

    [Fact]
    public async Task StripeReadFailureLeavesOrderPending()
    {
        _stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_test", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("fixture failure"));
        Assert.Equal(503, status(await order().ValidateStripeSession(100)));
        Assert.Equal("Pending", _db.OrderHeaders.AsNoTracking().Single().Status); Assert.Empty(_bus.Invocations);
    }

    [Fact]
    public async Task RewardFailureReportsPartialCompletionWithoutReopeningOrAutomaticRepublish()
    {
        payment(); _bus.Setup(b => b.PublishMessage(It.IsAny<object>(), It.IsAny<string>())).ThrowsAsync(new HttpRequestException("fixture failure"));
        Assert.Equal(503, status(await order().ValidateStripeSession(100)));
        Assert.Equal("Approved", _db.OrderHeaders.AsNoTracking().Single().Status);
        Assert.True((await order().ValidateStripeSession(100)).Value.IsSuccess);
        Assert.Single(_bus.Invocations);
    }

    private void payment(string mismatch = "")
    {
        var session = new Session { Id = mismatch == "session" ? "cs_other" : "cs_test", ClientReferenceId = mismatch == "reference" ? "200" : "100",
            AmountTotal = mismatch == "amount" ? 1 : 3498, Currency = mismatch == "currency" ? "eur" : "usd",
            PaymentStatus = mismatch == "unpaid" ? "unpaid" : "paid", PaymentIntentId = "pi_test" };
        _stripe.Setup(s => s.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_test", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        if (mismatch is "reference" or "session" or "amount" or "currency" or "unpaid") return;
        _stripe.Setup(s => s.RequestAsync<PaymentIntent>(HttpMethod.Get, "/v1/payment_intents/pi_test", It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentIntent { Id = mismatch == "intentId" ? "pi_other" : "pi_test", Status = mismatch == "intentStatus" ? "processing" : "succeeded",
                Currency = mismatch == "intentCurrency" ? "eur" : "usd", AmountReceived = mismatch == "intentAmount" ? 1 : 3498 });
    }

    private static CartDtos.CartDto cartInput(int count) => new() { CartHeader = new() { UserId = "alice" },
        CartDetails = new[] { new CartDtos.CartDetailsDto { ProductId = 1, Count = count } } };
    private static OrderDtos.CartDto orderInput() => new() { CartHeader = new() { UserId = "alice", CartHeaderId = 100, CouponCode = "SAVE", CartTotal = 0.01, Discount = 900 },
        CartDetails = new[] { new OrderDtos.CartDetailsDto { ProductId = 1, Count = 2, Product = new() { ProductId = 1, Name = "Forged", Price = 0.01 } } } };
    private static OrderDtos.StripeRequestDto stripeInput() => new() { ApprovedUrl = "https://example.invalid/success", CancelUrl = "https://example.invalid/cancel",
        OrderHeader = new() { OrderHeaderId = 100, UserId = "alice", OrderTotal = 0.01, Discount = 0, OrderDetails = Array.Empty<OrderDtos.OrderDetailsDto>() } };
    private CartController cart() => new(_cart, new Mango.Services.ShoppingCartAPI.MappingConfig(), _cartProducts.Object, _cartCoupons.Object, _bus.Object, _config)
        { ControllerContext = context() };
    private OrderAPIController order() => new(_db, _products.Object, new Mango.Services.OrderAPI.MappingConfig(), _config, _bus.Object, _coupons.Object)
        { ControllerContext = context() };
    private static ControllerContext context() => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim(ClaimTypes.Role, "ADMIN") }, "test")) } };
    private static int status<T>(ActionResult<T> result) => Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode.Value;
    public void Dispose()
    {
        try { _stripe.VerifyAll(); _stripe.VerifyNoOtherCalls(); }
        finally { StripeConfiguration.StripeClient = _previousStripe; _cart.Dispose(); _db.Dispose(); }
    }
}
