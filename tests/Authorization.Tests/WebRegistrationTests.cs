using Mango.Web.Controllers;
using Mango.Web.Models;
using Mango.Web.Service.IService;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Xunit;

namespace Authorization.Tests;

public class WebRegistrationTests
{
    [Fact]
    public async Task PublicRegistrationDoesNotCallTheAdministrativeRoleEndpoint()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(s => s.RegisterAsync(It.IsAny<RegistrationRequestDto>()))
            .ReturnsAsync(new ResponseDto { IsSuccess = true });
        var context = new DefaultHttpContext();
        var controller = new AuthController(auth.Object, new Mock<ITokenProvider>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new Mock<ITempDataProvider>().Object)
        };
        var result = Assert.IsType<RedirectToActionResult>(await controller.Register(new RegistrationRequestDto
        {
            Email = "learner@example.invalid", Password = "NotARealCredential!123", Role = "ADMIN"
        }));
        Assert.Equal("Login", result.ActionName);
        auth.Verify(s => s.RegisterAsync(It.IsAny<RegistrationRequestDto>()), Times.Once);
        auth.Verify(s => s.AssignRoleAsync(It.IsAny<RegistrationRequestDto>()), Times.Never);
    }
}
