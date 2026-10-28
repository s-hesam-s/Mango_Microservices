using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mango.Web.Configuration;
using Mango.Web.Controllers;
using Xunit;

namespace Authorization.Tests;

public class WebSecurityTests
{
    [Theory]
    [InlineData("missing", HttpStatusCode.BadRequest)]
    [InlineData("invalid", HttpStatusCode.BadRequest)]
    [InlineData("valid", HttpStatusCode.OK)]
    public async Task ProductionMvcRegistrationValidatesAntiforgery(string tokenKind, HttpStatusCode expected)
    {
        using var server = createServer();
        using var client = server.CreateClient();
        var tokenResponse = await client.GetAsync("/security-probe/token");
        tokenResponse.EnsureSuccessStatusCode();
        var cookie = tokenResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        var fields = new Dictionary<string, string>();
        if (tokenKind != "missing") fields["__RequestVerificationToken"] = tokenKind == "valid" ? token : "invalid";
        var response = await client.PostAsync("/security-probe/write", new FormUrlEncodedContent(fields));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task LogoutCannotBeInvokedWithGet()
    {
        using var server = createServer();
        using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/Auth/Logout")).StatusCode);
    }

    private static TestServer createServer()
    {
        return new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddSecureMvc().AddApplicationPart(typeof(SecurityProbeController).Assembly)
                .AddApplicationPart(typeof(AuthController).Assembly);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapDefaultControllerRoute());
        }));
    }
}

[Route("security-probe")]
public class SecurityProbeController : Controller
{
    [HttpGet("token")]
    public IActionResult Token([FromServices] IAntiforgery antiforgery) =>
        Json(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("write")]
    public IActionResult Write() => Ok();
}
