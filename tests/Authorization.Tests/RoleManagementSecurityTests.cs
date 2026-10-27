using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Mango.MessageBus;
using Mango.Services.AuthAPI.Controllers;
using Mango.Services.AuthAPI.Models;
using Mango.Services.AuthAPI.Models.Dto;
using Mango.Services.AuthAPI.Service;
using Mango.Services.AuthAPI.Service.IService;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class RoleManagementSecurityTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("CUSTOMER", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    public async Task RoleManagementRequiresAnAuthenticatedAdministrator(string role, HttpStatusCode expected)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(s => s.AssignRole(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        using var server = createServer(auth);
        using var client = server.CreateClient();
        if (role != null)
        {
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + role);
        }
        using var response = await client.PostAsJsonAsync("/api/auth/AssignRole",
            new RoleAssignmentRequestDto { Email = "learner@example.invalid", Role = "CUSTOMER" });
        Assert.Equal(expected, response.StatusCode);
        auth.Verify(s => s.AssignRole(It.IsAny<string>(), It.IsAny<string>()),
            role == "ADMIN" ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ROOT")]
    [InlineData("ADMINISTRATOR")]
    public async Task UnknownRolesAreRejectedBeforeLookingUpAUser(string role)
    {
        var users = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object,
            null, null, null, null, null, null, null, null);
        var roles = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object,
            null, null, null, null);
        var service = new AuthService(null, null, users.Object, roles.Object);
        Assert.False(await service.AssignRole("learner@example.invalid", role));
        users.Verify(u => u.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        roles.Verify(r => r.CreateAsync(It.IsAny<IdentityRole>()), Times.Never);
    }

    [Fact]
    public async Task IdentityAssignmentFailureIsReturnedToTheCaller()
    {
        var user = new ApplicationUser { Email = "learner@example.invalid" };
        var users = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object,
            null, null, null, null, null, null, null, null);
        users.Setup(u => u.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        users.Setup(u => u.AddToRoleAsync(user, "CUSTOMER"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Assignment failed" }));
        var roles = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object,
            null, null, null, null);
        roles.Setup(r => r.RoleExistsAsync("CUSTOMER")).ReturnsAsync(true);
        var service = new AuthService(null, null, users.Object, roles.Object);
        Assert.False(await service.AssignRole(user.Email, "customer"));
    }

    private static TestServer createServer(Mock<IAuthService> auth)
    {
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddSingleton(auth.Object);
                services.AddSingleton(new Mock<IMessageBus>().Object);
                services.AddControllers().AddApplicationPart(typeof(AuthAPIController).Assembly);
                services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    private class TestBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, header[7..]) }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
