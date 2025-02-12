namespace Assignment2.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Assignment2.Data;
    using System.Security.Cryptography;
    using System.Text.RegularExpressions;
    using System.Text;
    using System.IO;
    using System.Threading.Tasks;
    using Assignment2.Models;
    using System.Net.Mail;
    using System.Net;

    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]  // CSRF protection
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Sanitize input to prevent XSS
            model.FirstName = SanitizeInput(model.FirstName);
            model.LastName = SanitizeInput(model.LastName);
            model.WhoAmI = SanitizeInput(model.WhoAmI);

            // Email validation
            if (!IsValidEmail(model.Email))
            {
                ModelState.AddModelError("Email", "Invalid email format.");
                return View(model);
            }

            // Check for duplicate email
            if (await _context.Members.AnyAsync(m => m.Email == model.Email))
            {
                ModelState.AddModelError("Email", "Email is already in use.");
                return View(model);
            }

            // Encrypt NRIC
            var (encryptedNRIC, encryptionKey, encryptionIV) = EncryptData(model.NRIC);

            // Hash the password
            string hashedPassword = HashPassword(model.Password);

            // Save the uploaded resume securely
            string sanitizedFileName = SanitizeFileName(model.Resume.FileName);
            string resumePath = Path.Combine("wwwroot/uploads", sanitizedFileName);
            using (var stream = new FileStream(resumePath, FileMode.Create))
            {
                await model.Resume.CopyToAsync(stream);
            }

            var member = new Member
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Gender = model.Gender,
                EncryptedNRIC = encryptedNRIC,
                Email = model.Email,
                PasswordHash = hashedPassword,
                DateOfBirth = model.DateOfBirth,
                ResumePath = resumePath,
                WhoAmI = model.WhoAmI,
                EncryptionKey = encryptionKey,
                EncryptionIV = encryptionIV,
                SessionId = string.Empty
            };

            _context.Members.Add(member);
            await _context.SaveChangesAsync();

            return RedirectToAction("Login", "Account");
        }

        private (string EncryptedData, string Key, string IV) EncryptData(string input)
        {
            using (var aes = Aes.Create())
            {
                aes.GenerateKey();
                aes.GenerateIV();

                var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var writer = new StreamWriter(cs))
                {
                    writer.Write(input);
                }

                string encryptedData = Convert.ToBase64String(ms.ToArray());
                string key = Convert.ToBase64String(aes.Key);
                string iv = Convert.ToBase64String(aes.IV);

                return (encryptedData, key, iv);
            }
        }
        private string DecryptData(string encryptedData, string key, string iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(key);
                aes.IV = Convert.FromBase64String(iv);

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(Convert.FromBase64String(encryptedData));
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var reader = new StreamReader(cs);

                return reader.ReadToEnd();
            }
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hash);
            }
        }

        private string SanitizeInput(string input)
        {
            return Regex.Replace(input, @"<.*?>", string.Empty);  // Remove HTML tags to prevent XSS
        }

        private string SanitizeFileName(string fileName)
        {
            return Regex.Replace(fileName, @"[^\w\.-]", "_");  // Replace invalid characters with underscores
        }

        private bool IsValidEmail(string email)
        {
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Members.FirstOrDefaultAsync(m => m.Email == model.Email);
            if (user == null || !VerifyPasswordHash(model.Password, user.PasswordHash))
            {
                ViewData["ErrorMessage"] = "Incorrect email or password.";
                return View(model);
            }

            // Prevent multiple sessions
            if (!string.IsNullOrEmpty(user.SessionId))
            {
                ViewData["ErrorMessage"] = "You are already logged in from another device.";
                return View(model);
            }

            // Handle account lockout
            if (user.LockoutEndTime.HasValue && user.LockoutEndTime > DateTime.UtcNow)
            {
                ViewData["ErrorMessage"] = $"Your account is locked. Try again in {user.LockoutEndTime.Value - DateTime.UtcNow:mm\\:ss} minutes.";
                return View(model);
            }

            // ✅ If 2FA is enabled, generate OTP and send it via email
            if (user.TwoFactorEnabled)
            {
                string otpCode = GenerateOtp();
                user.TwoFactorCode = otpCode;
                user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5); // OTP valid for 5 minutes
                await _context.SaveChangesAsync();

                await EmailService.SendEmailAsync(user.Email, "Your 2FA Code", $"Your OTP code is: {otpCode}");

                HttpContext.Session.SetString("Pending2FAUser", user.Email);
                ViewData["Require2FA"] = true;  // ✅ Tell the view to display the 2FA form
                return View("Login");  // ✅ Re-render the login page with 2FA enabled
            }

            // Successful login
            user.SessionId = Guid.NewGuid().ToString();
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("SessionId", user.SessionId);

            return RedirectToAction("Index", "Home");
        }

        // ✅ Two-Factor Authentication Page (OTP Entry)
        [HttpGet]
        public IActionResult TwoFactorAuth()
        {
            return View();
        }

        // ✅ Verify OTP Code
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TwoFactorAuth(TwoFactorAuthViewModel model)
        {
            string email = HttpContext.Session.GetString("Pending2FAUser");
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login");
            }

            var user = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            if (user.TwoFactorCode != model.Code.Trim() || user.TwoFactorExpiry < DateTime.UtcNow)
            {
                ViewData["ErrorMessage"] = "Invalid or expired OTP code.";
                return View("Login");
            }

            // ✅ Successful 2FA verification
            user.SessionId = Guid.NewGuid().ToString();
            user.TwoFactorCode = null;
            user.TwoFactorExpiry = null;
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("SessionId", user.SessionId);

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableTwoFactorAuth()
        {
            string userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var user = await _context.Members.FindAsync(userId);
            if (user == null) return RedirectToAction("Login");

            // ✅ Enable 2FA directly
            user.TwoFactorEnabled = true;
            await _context.SaveChangesAsync();

            ViewData["Message"] = "Two-Factor Authentication has been enabled.";
            return RedirectToAction("Index", "Home");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableTwoFactorAuth()
        {
            string userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var user = await _context.Members.FindAsync(userId);
            if (user == null) return RedirectToAction("Login");

            // ✅ Disable 2FA directly
            user.TwoFactorEnabled = false;
            user.TwoFactorCode = null;
            user.TwoFactorExpiry = null;
            await _context.SaveChangesAsync();

            ViewData["Message"] = "Two-Factor Authentication has been disabled.";
            return RedirectToAction("Index", "Home");
        }

        // ✅ Generate OTP & Expiry
        private string GenerateOtp()
        {
            Random random = new Random();
            return random.Next(100000, 999999).ToString(); // 6-digit OTP
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            string sessionId = HttpContext.Session.GetString("SessionId");
            if (!string.IsNullOrEmpty(sessionId))
            {
                int userId = int.Parse(HttpContext.Session.GetString("UserId"));
                var user = await _context.Members.FirstOrDefaultAsync(m => m.Id == userId);

                if (user != null && user.SessionId == sessionId)
                {
                    user.SessionId = string.Empty;
                    user.TwoFactorCode = null; // ✅ Clear OTP on logout
                    user.TwoFactorExpiry = null;

                    // Explicitly mark the SessionId property as modified
                    _context.Entry(user).Property(u => u.SessionId).IsModified = true;

                    await _context.SaveChangesAsync();
                }
            }

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        private bool IsPasswordReused(string newPassword, Member user)
        {
            string newHash = HashPassword(newPassword);
            return newHash == user.PasswordHash || newHash == user.OldPasswordHash1 || newHash == user.OldPasswordHash2;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Ensure session exists before trying to get UserId
            string userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login");
            }

            var user = await _context.Members.FindAsync(userId);

            if (user == null)
            {
                return RedirectToAction("Login");
            }

            // Ensure the new password is not one of the last two used passwords
            string newHash = HashPassword(model.NewPassword);
            if (newHash == user.PasswordHash || newHash == user.OldPasswordHash1 || newHash == user.OldPasswordHash2)
            {
                ViewData["ErrorMessage"] = "This password has been used before. Please choose a different password.";
                return View(model);
            }

            // Rotate old password hashes before updating
            user.OldPasswordHash2 = user.OldPasswordHash1;
            user.OldPasswordHash1 = user.PasswordHash;
            user.PasswordHash = newHash;
            user.LastPasswordChange = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Members.FirstOrDefaultAsync(m => m.Email == model.Email);
            if (user == null)
            {
                ViewData["Message"] = "If the email exists, a reset link has been sent.";
                return View();
            }

            // Generate a reset token
            user.ResetToken = Guid.NewGuid().ToString();
            user.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(15); // 15-minute expiry
            await _context.SaveChangesAsync();

            // Create reset link
            string resetLink = Url.Action("ResetPassword", "Account", new { token = user.ResetToken }, Request.Scheme);
            string emailBody = $"Click the link to reset your password: <a href='{resetLink}'>Reset Password</a>";

            // Send email
            await EmailService.SendEmailAsync(user.Email, "Password Reset", emailBody);

            ViewData["Message"] = "If the email exists, a reset link has been sent.";
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> SendPasswordResetLink(string email)
        {
            var user = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            if (user == null)
            {
                ViewData["ErrorMessage"] = "No account associated with this email.";
                return View("ForgotPassword");
            }

            // Generate unique reset token
            user.ResetToken = Guid.NewGuid().ToString();
            user.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(15); // 15 minutes expiry

            await _context.SaveChangesAsync();

            string resetLink = Url.Action("ResetPassword", "Account", new { token = user.ResetToken }, Request.Scheme);
            await EmailService.SendEmailAsync(user.Email, "Password Reset", $"Click here to reset your password: {resetLink}");

            return View("ResetLinkSent");
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            var user = await _context.Members.FirstOrDefaultAsync(m => m.ResetToken == token && m.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null)
            {
                return View("Error"); // Show an error if token is invalid or expired
            }

            return View(new ResetPasswordViewModel { Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            var user = await _context.Members.FirstOrDefaultAsync(m => m.ResetToken == model.Token && m.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null)
            {
                ViewData["ErrorMessage"] = "Invalid or expired reset token.";
                return View(model);
            }

            // Hash the new password
            string newHash = HashPassword(model.NewPassword);

            // Ensure the new password is not one of the last two used passwords
            if (newHash == user.PasswordHash || newHash == user.OldPasswordHash1 || newHash == user.OldPasswordHash2)
            {
                ViewData["ErrorMessage"] = "This password has been used before. Please use a different password.";
                return View(model);
            }

            // Update old password history before changing the password
            user.OldPasswordHash2 = user.OldPasswordHash1;
            user.OldPasswordHash1 = user.PasswordHash;
            user.PasswordHash = newHash;
            user.LastPasswordChange = DateTime.UtcNow;
            user.ResetToken = null;
            user.ResetTokenExpiry = null;

            await _context.SaveChangesAsync();
            return RedirectToAction("Login");
        }

        public static class EmailService
        {
            public static async Task SendEmailAsync(string to, string subject, string body)
            {
                using var smtp = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential("soonfook7@gmail.com", "fycc inoe wvyf coxr"), // Use your Gmail credentials
                    EnableSsl = true
                };


                var mailMessage = new MailMessage
                {
                    From = new MailAddress("your-email@gmail.com"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(to);

                try
                {
                    await smtp.SendMailAsync(mailMessage);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Email failed to send: {ex.Message}");
                }
            }
        }

        private bool VerifyPasswordHash(string password, string storedHash)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hash) == storedHash;
            }
        }
    }
}
