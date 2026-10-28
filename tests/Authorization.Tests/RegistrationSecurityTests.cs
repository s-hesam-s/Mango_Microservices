using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using Mango.Services.AuthAPI.Models;
using Mango.Services.AuthAPI.Models.Dto;
using Mango.Services.AuthAPI.Service;

namespace Authorization.Tests;

public class RegistrationSecurityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("unexpected-role")]
    public async Task RegistrationAlwaysAssignsOnlyTheCustomerRole(string requestedRole)
    {
        var users = createUsers();
        var roles = createRoles();
        var service = createService(users, roles);
        var result = await service.Register(new RegistrationRequestDto { Email = "learner@example.invalid", Password = "NotARealCredential!123", Role = requestedRole });
        Assert.Equal(string.Empty, result);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "CUSTOMER"), Times.Once);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.Is<string>(r => r != "CUSTOMER")), Times.Never);
        roles.Verify(r => r.CreateAsync(It.Is<IdentityRole>(r => r.Name != "CUSTOMER")), Times.Never);
    }

    [Fact]
    public async Task FailedCustomerRoleAssignmentDoesNotReportSuccessfulRegistration()
    {
        var users = createUsers();
        var roles = createRoles();
        users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "CUSTOMER"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Assignment failed" }));
        var service = createService(users, roles);
        string requestedRole = "admin";
        var result = await service.Register(new RegistrationRequestDto { Email = "learner@example.invalid", Password = "NotARealCredential!123", Role = requestedRole });
        Assert.NotEqual(string.Empty, result);
        users.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Fact]
    public async Task FailedRoleCreationDoesNotAssignAnyRole()
    {
        var users = createUsers();
        var roles = createRoles();
        roles.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Creation failed" }));
        var service = createService(users, roles);
        string requestedRole = "admin";
        var result = await service.Register(new RegistrationRequestDto { Email = "learner@example.invalid", Password = "NotARealCredential!123", Role = requestedRole });
        Assert.NotEqual(string.Empty, result);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        users.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    private static Mock<UserManager<ApplicationUser>> createUsers()
    {
        var manager = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object,
            null, null, null, null, null, null, null, null);
        manager.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        return manager;
    }

    private static Mock<RoleManager<IdentityRole>> createRoles()
    {
        var manager = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object,
            null, null, null, null);
        manager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        manager.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);
        return manager;
    }

    private static AuthService createService(Mock<UserManager<ApplicationUser>> users, Mock<RoleManager<IdentityRole>> roles)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new AuthService(null, null, users.Object, roles.Object);
    }
}
