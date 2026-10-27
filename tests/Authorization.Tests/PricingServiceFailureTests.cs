using System.Net;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class PricingServiceFailureTests
{
    [Theory]
    [InlineData("cartProduct")] [InlineData("orderProduct")] [InlineData("cartCoupon")] [InlineData("orderCoupon")]
    public async Task UpstreamHttpErrorsCannotMasqueradeAsSuccessfulCatalogOrCoupon(string service)
    {
        var factory = factoryFor(HttpStatusCode.ServiceUnavailable, "{}");
        await Assert.ThrowsAsync<HttpRequestException>(async () => {
            switch (service) {
                case "cartProduct": await new Mango.Services.ShoppingCartAPI.Service.ProductService(factory).GetProducts(); break;
                case "orderProduct": await new Mango.Services.OrderAPI.Service.ProductService(factory).GetProducts(); break;
                case "cartCoupon": await new Mango.Services.ShoppingCartAPI.Service.CouponService(factory).GetCoupon("SAVE"); break;
                default: await new Mango.Services.OrderAPI.Service.CouponService(factory).GetCoupon("SAVE"); break;
            }
        });
    }

    [Fact]
    public async Task UnsuccessfulProductEnvelopeDoesNotBecomeAnEmptyCatalog()
    {
        var factory = factoryFor(HttpStatusCode.OK, "{\"isSuccess\":false}");
        await Assert.ThrowsAsync<HttpRequestException>(() => new Mango.Services.ShoppingCartAPI.Service.ProductService(factory).GetProducts());
        await Assert.ThrowsAsync<HttpRequestException>(() => new Mango.Services.OrderAPI.Service.ProductService(factory).GetProducts());
    }

    private static IHttpClientFactory factoryFor(HttpStatusCode status, string body)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new FixtureHandler(status, body)) { BaseAddress = new Uri("https://example.invalid") });
        return factory.Object;
    }

    private class FixtureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
