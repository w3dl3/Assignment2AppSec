namespace Assignment2.Models
{
    using System.ComponentModel.DataAnnotations;

    public class TwoFactorAuthViewModel
    {
        [Required(ErrorMessage = "OTP Code is required.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP Code must be 6 digits.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP Code must be exactly 6 digits.")]
        public string Code { get; set; }
    }
}
