using Mango.Services.AuthAPI.Models.Dto;
using Mango.Services.AuthAPI.Models;
using Mango.Services.AuthAPI.Service.IService;
using Microsoft.AspNetCore.Identity;
using Mango.Services.AuthAPI.Data;

namespace Mango.Services.AuthAPI.Service
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext  _db;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AuthService(AppDbContext db, IJwtTokenGenerator jwtTokenGenerator,
            UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _jwtTokenGenerator = jwtTokenGenerator;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<bool> AssignRole(string email, string roleName)
        {
            var normalizedRole = roleName?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(email) || (normalizedRole != "ADMIN" && normalizedRole != "CUSTOMER"))
            {
                return false;
            }
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                return false;
            }
            if (!await _roleManager.RoleExistsAsync(normalizedRole))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole(normalizedRole));
                if (!roleResult.Succeeded)
                {
                    return false;
                }
            }
            return (await _userManager.AddToRoleAsync(user, normalizedRole)).Succeeded;
        }

        public async Task<LoginResponseDto> Login(LoginRequestDto loginRequestDto)
        {
            var user = _db.ApplicationUsers.FirstOrDefault(u => u.UserName.ToLower() == loginRequestDto.UserName.ToLower());

            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequestDto.Password);

            if (user == null || isValid == false)
            {
                return new LoginResponseDto() { User = null, Token = "" };
            }

            //if user was found , Generate JWT Token
            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtTokenGenerator.GenerateToken(user, roles);

            UserDto userDTO = new()
            {
                Email = user.Email,
                ID = user.Id,
                Name = user.Name,
                PhoneNumber = user.PhoneNumber
            };

            LoginResponseDto loginResponseDto = new LoginResponseDto()
            {
                User = userDTO,
                Token = token
            };

            return loginResponseDto;
        }

        public async Task<string> Register(RegistrationRequestDto registrationRequestDto)
        {
            ApplicationUser user = new()
            {
                UserName = registrationRequestDto.Email,
                Email = registrationRequestDto.Email,
                NormalizedEmail = registrationRequestDto.Email.ToUpper(),
                Name = registrationRequestDto.Name,
                PhoneNumber = registrationRequestDto.PhoneNumber
            };

            try
            {
                var result = await _userManager.CreateAsync(user, registrationRequestDto.Password);
                if (result.Succeeded)
                {
                    const string customerRole = "CUSTOMER";
                    if (!await _roleManager.RoleExistsAsync(customerRole))
                    {
                        var roleResult = await _roleManager.CreateAsync(new IdentityRole(customerRole));
                        if (!roleResult.Succeeded)
                        {
                            await _userManager.DeleteAsync(user);
                            return "Unable to create the customer role.";
                        }
                    }
                    var assignmentResult = await _userManager.AddToRoleAsync(user, customerRole);
                    if (!assignmentResult.Succeeded)
                    {
                        await _userManager.DeleteAsync(user);
                        return "Unable to assign the customer role.";
                    }
                    return "";
                }
                else
                {
                    return result.Errors.FirstOrDefault().Description;
                }
            }
            catch (Exception ex)
            {

            }
            return "Error Encountered";
        }
    }
}
