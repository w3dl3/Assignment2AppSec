namespace Assignment2.Models
{
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Member
{
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string FirstName { get; set; }

        [Required, MaxLength(50)]
        public string LastName { get; set; }

        [Required]
        public string Gender { get; set; }

        [Required]
        public string EncryptedNRIC { get; set; }  // Encrypted NRIC

        [Required, EmailAddress]
        public string Email { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LastFailedLogin { get; set; }
        public DateTime? LockoutEndTime { get; set; }

        [Required]
        public string PasswordHash { get; set; }
        public string? OldPasswordHash1 { get; set; } = string.Empty;
        public string? OldPasswordHash2 { get; set; } = string.Empty;
        public DateTime LastPasswordChange { get; set; } = DateTime.UtcNow;
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public bool TwoFactorEnabled { get; set; } = false; // Enable/Disable 2FA
        public string? TwoFactorCode { get; set; } // Stores the OTP
        public DateTime? TwoFactorExpiry { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime DateOfBirth { get; set; }

        [Required]
        public string ResumePath { get; set; }  // Path to uploaded resume file

        [MaxLength(500)]
        public string WhoAmI { get; set; }
        public string EncryptionKey { get; set; }
        public string EncryptionIV { get; set; }
        public string SessionId { get; set; }
    }
}
