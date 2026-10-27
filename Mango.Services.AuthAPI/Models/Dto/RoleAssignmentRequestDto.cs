using System.ComponentModel.DataAnnotations;

namespace Mango.Services.AuthAPI.Models.Dto
{
    public class RoleAssignmentRequestDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;
    }
}
