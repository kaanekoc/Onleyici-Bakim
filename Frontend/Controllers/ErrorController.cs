using Microsoft.AspNetCore.Mvc;
using OnleyiciBakimSistemi.Models;

namespace OnleyiciBakimSistemi.Controllers;

// UseStatusCodePagesWithReExecute("/Error/{0}") bu controller'a düşer.
// Örn: olmayan bir sayfa -> 404 -> Index(404) -> Views/Error/NotFound.cshtml
//      sunucu hatası    -> 500 -> Index(500) -> Views/Error/Generic.cshtml
[Route("Error")]
public class ErrorController : Controller
{
    [Route("{code:int}")]
    public IActionResult Index(int code)
    {
        Response.StatusCode = code;

        var vm = new ErrorViewModel
        {
            RequestId = HttpContext.TraceIdentifier,
            StatusCode = code,
        };

        if (code == 404)
        {
            return View("NotFound", vm);
        }

        return View("Generic", vm);
    }
}
