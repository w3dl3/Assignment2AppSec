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

        private static Dictionary<string, int> failedLoginAttempts = new();

        [HttpPost]
        [ValidateAntiForgeryToken]  // CSRF protection
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string email = model.Email.ToLower();

            // Check if account is locked
            if (failedLoginAttempts.ContainsKey(email) && failedLoginAttempts[email] >= 3)
            {
                ViewData["ErrorMessage"] = "Your account has been locked due to too many failed login attempts. Please try again later.";
                return View(model);
            }

            // Check if the user exists
            var user = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            if (user == null || !VerifyPasswordHash(model.Password, user.PasswordHash))
            {
                // Increment failed login attempts
                if (failedLoginAttempts.ContainsKey(email))
                {
                    failedLoginAttempts[email]++;
                }
                else
                {
                    failedLoginAttempts[email] = 1;
                }

                ViewData["ErrorMessage"] = "Incorrect email or password.";
                return View(model);
            }

            // Check if user already has an active session
            if (!string.IsNullOrEmpty(user.SessionId))
            {
                ViewData["ErrorMessage"] = "You are already logged in on another device. Please log out before logging in again.";
                return View(model);
            }

            // Generate a new session ID
            string sessionId = Guid.NewGuid().ToString();

            // Update the user's session ID in the database
            user.SessionId = sessionId;
            await _context.SaveChangesAsync();

            // Successful login - reset failed attempts and create session
            failedLoginAttempts[email] = 0;
            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("SessionId", sessionId);

            // Log user activity (audit log)
            await _context.AuditLog.AddAsync(new AuditLog
            {
                UserId = user.Id,
                Activity = "User logged in",
                Timestamp = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Home");
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

                    // Explicitly mark the SessionId property as modified
                    _context.Entry(user).Property(u => u.SessionId).IsModified = true;

                    await _context.SaveChangesAsync();
                }
            }

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
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
