using System.Security.Claims;
using Mango.Web.Controllers;
using Mango.Web.Models;
using Mango.Web.Service.IService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class CheckoutFailureTests
{
    [Theory]
    [InlineData("cartNull")] [InlineData("cartFailure")] [InlineData("cartMalformed")] [InlineData("cartThrows")]
    [InlineData("orderNull")] [InlineData("orderFailure")] [InlineData("orderMalformed")] [InlineData("orderMissing")]
    [InlineData("stripeNull")] [InlineData("stripeFailure")] [InlineData("stripeMalformed")] [InlineData("stripeMissing")]
    [InlineData("stripeUrl")] [InlineData("stripeThrows")]
    public async Task CheckoutFailureReturnsRenderableModelWithoutRedirect(string failure)
    {
        var (controller, cart, orders) = fixture();
        if (failure.StartsWith("cart")) cart.Setup(s => s.GetCartByUserIdAsnyc("alice")).ReturnsAsync(response(failure));
        if (failure == "cartThrows") cart.Setup(s => s.GetCartByUserIdAsnyc("alice")).ThrowsAsync(new HttpRequestException("fixture"));
        if (failure.StartsWith("order")) orders.Setup(s => s.CreateOrder(It.IsAny<CartDto>())).ReturnsAsync(response(failure));
        if (failure.StartsWith("stripe")) orders.Setup(s => s.CreateStripeSession(It.IsAny<StripeRequestDto>())).ReturnsAsync(response(failure));
        if (failure == "stripeThrows") orders.Setup(s => s.CreateStripeSession(It.IsAny<StripeRequestDto>())).ThrowsAsync(new HttpRequestException("fixture"));
        var result = Assert.IsType<ViewResult>(await controller.Checkout(validCart()));
        var model = Assert.IsType<CartDto>(result.Model);
        Assert.NotNull(model.CartHeader); Assert.NotNull(model.CartDetails);
        Assert.False(controller.ModelState.IsValid); Assert.False(controller.Response.Headers.ContainsKey("Location"));
        if (failure.StartsWith("cart")) orders.Verify(s => s.CreateOrder(It.IsAny<CartDto>()), Times.Never);
        if (!failure.StartsWith("stripe")) orders.Verify(s => s.CreateStripeSession(It.IsAny<StripeRequestDto>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulCheckoutRedirectsToReturnedHttpsSession()
    {
        var (controller, _, _) = fixture();
        var result = Assert.IsType<StatusCodeResult>(await controller.Checkout(validCart()));
        Assert.Equal(303, result.StatusCode); Assert.Equal("https://checkout.stripe.com/test", controller.Response.Headers.Location);
    }

    [Theory]
    [InlineData("null")] [InlineData("failure")] [InlineData("malformed")] [InlineData("pending")] [InlineData("wrongOrder")] [InlineData("throws")]
    public async Task FailedConfirmationNeverShowsSuccess(string failure)
    {
        var (controller, _, orders) = fixture();
        orders.Setup(s => s.ValidateStripeSession(100)).ReturnsAsync(failure switch {
            "null" => null, "failure" => new ResponseDto { IsSuccess = false }, "malformed" => new ResponseDto { Result = "{" },
            _ => new ResponseDto { Result = new OrderHeaderDto { OrderHeaderId = failure == "wrongOrder" ? 200 : 100, Status = "Pending" } }
        });
        if (failure == "throws") orders.Setup(s => s.ValidateStripeSession(100)).ThrowsAsync(new HttpRequestException("fixture"));
        var result = Assert.IsType<ViewResult>(await controller.Confirmation(100));
        Assert.Equal("Confirmation", result.ViewName); Assert.NotNull(controller.ViewData["PaymentError"]);
    }

    [Theory]
    [InlineData("Approved")] [InlineData("ReadyForPickup")] [InlineData("Completed")]
    public async Task ConfirmedOrdersShowSuccessWithoutError(string status)
    {
        var (controller, _, orders) = fixture();
        orders.Setup(s => s.ValidateStripeSession(100)).ReturnsAsync(new ResponseDto { Result = new OrderHeaderDto { OrderHeaderId = 100, Status = status } });
        Assert.IsType<ViewResult>(await controller.Confirmation(100)); Assert.Null(controller.ViewData["PaymentError"]);
    }

    private static ResponseDto response(string failure)
    {
        if (failure.EndsWith("Null")) return null;
        if (failure.EndsWith("Failure")) return new ResponseDto { IsSuccess = false };
        if (failure.EndsWith("Malformed")) return new ResponseDto { Result = "{" };
        if (failure == "stripeUrl") return new ResponseDto { Result = new StripeRequestDto { StripeSessionUrl = "javascript:alert(1)" } };
        return new ResponseDto();
    }

    private static CartDto validCart() => new() { CartHeader = new() { UserId = "alice", Name = "Fixture", Email = "test@example.invalid", Phone = "fixture" },
        CartDetails = new[] { new CartDetailsDto { ProductId = 1, Count = 1 } } };

    private static (CartController, Mock<ICartService>, Mock<IOrderService>) fixture()
    {
        var cart = new Mock<ICartService>(); var orders = new Mock<IOrderService>();
        cart.Setup(s => s.GetCartByUserIdAsnyc("alice")).ReturnsAsync(new ResponseDto { Result = validCart() });
        orders.Setup(s => s.CreateOrder(It.IsAny<CartDto>())).ReturnsAsync(new ResponseDto { Result = new OrderHeaderDto { OrderHeaderId = 100, Status = "Pending" } });
        orders.Setup(s => s.CreateStripeSession(It.IsAny<StripeRequestDto>())).ReturnsAsync(new ResponseDto {
            Result = new StripeRequestDto { StripeSessionUrl = "https://checkout.stripe.com/test" } });
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "alice") }, "test")) };
        context.Request.Scheme = "https"; context.Request.Host = new HostString("example.invalid");
        var controller = new CartController(cart.Object, orders.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
        return (controller, cart, orders);
    }
}
