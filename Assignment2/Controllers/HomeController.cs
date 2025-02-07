namespace Assignment2.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Http;
    using Assignment2.Data;
    using Assignment2.Models;
    using Microsoft.Extensions.Logging;
    using System.Diagnostics;
    using System.Threading.Tasks;

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
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserId")))
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = int.Parse(HttpContext.Session.GetString("UserId"));
            var user = await _context.Members.FindAsync(userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var userInfo = new UserInfoViewModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Gender = user.Gender,
                EncryptedNRIC = user.EncryptedNRIC,
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
