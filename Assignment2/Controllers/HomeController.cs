namespace Assignment2.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Http;
    using Assignment2.Data;
    using Assignment2.Models;
    using Microsoft.Extensions.Logging;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Security.Cryptography;

    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;

        public HomeController(ILogger<HomeController> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            string sessionId = HttpContext.Session.GetString("SessionId");
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")) || string.IsNullOrEmpty(sessionId))
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = int.Parse(HttpContext.Session.GetString("UserId"));
            var user = await _context.Members.FindAsync(userId);

            if (user == null || user.SessionId != sessionId)
            {
                // Invalidate the session if it doesn't match
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            string decryptedNRIC = DecryptData(user.EncryptedNRIC, user.EncryptionKey, user.EncryptionIV);

            var userInfo = new UserInfoViewModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Gender = user.Gender,
                EncryptedNRIC = decryptedNRIC,
                Email = user.Email,
                DateOfBirth = user.DateOfBirth,
                WhoAmI = user.WhoAmI,
                ResumePath = user.ResumePath
            };

            return View(userInfo);
        }

        public IActionResult Privacy()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
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

        public IActionResult Error404()
        {
            Response.StatusCode = 404;
            return View("Error404");
        }

        public IActionResult Error403()
        {
            Response.StatusCode = 403;
            return View("Error403");
        }
    }
}
