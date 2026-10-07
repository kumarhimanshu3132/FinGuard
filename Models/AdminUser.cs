using System.ComponentModel.DataAnnotations;

namespace FinGuard.Models
{
    public class AdminUser
    {
        [Key]
        public int AdminId { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Email { get; set; } = string.Empty;

        public string? Password { get; set; }

        public string Role { get; set; } = "Admin";

        public string? FullName { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public bool IsVerified { get; set; } = false; 

        public string? OtpCode { get; set; }

        public DateTime? OtpExpiry { get; set; }
    }
}