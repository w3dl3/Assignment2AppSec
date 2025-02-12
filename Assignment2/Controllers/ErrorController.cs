using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Assignment2.Controllers
{
    public class ErrorController : Controller
    {
        private readonly ILogger<ErrorController> _logger;

        public ErrorController(ILogger<ErrorController> logger)
        {
            _logger = logger;
        }

        [Route("Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            _logger.LogWarning($"HTTP {statusCode} error occurred at {HttpContext.Request.Path}");

            var viewName = statusCode switch
            {
                (int)HttpStatusCode.NotFound => "404",
                (int)HttpStatusCode.Forbidden => "403",
                (int)HttpStatusCode.InternalServerError => "500",
                _ => "GeneralError"
            };

            ViewData["StatusCode"] = statusCode;
            return View(viewName);
        }

        [Route("Error/GeneralError")]
        public IActionResult GeneralError()
        {
            var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (exceptionFeature != null)
            {
                _logger.LogError($"Exception occurred at {exceptionFeature.Path}: {exceptionFeature.Error}");
            }

            return View("GeneralError");
        }
    }
}
