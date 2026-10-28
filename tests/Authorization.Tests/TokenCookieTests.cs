using Microsoft.AspNetCore.Http;
using Xunit;
namespace Authorization.Tests;

public class TokenCookieTests
{
    [Fact]
    public void BrowserTokenCookiesAreRestrictedToHttpsAndNotReadableByScripts()
    {
        var context = new DefaultHttpContext();
        var provider = new Mango.Web.Service.TokenProvider(new HttpContextAccessor { HttpContext = context });
        provider.SetToken("fixture-access");
        var cookies = context.Response.Headers.SetCookie;
        Assert.NotEmpty(cookies);
        foreach (var cookie in cookies)
        {
            var attributes = cookie.ToLowerInvariant();
            Assert.Contains("httponly", attributes);
            Assert.Contains("secure", attributes);
            Assert.Contains("samesite=lax", attributes);
            Assert.Contains("path=/", attributes);
        }
    }
}
