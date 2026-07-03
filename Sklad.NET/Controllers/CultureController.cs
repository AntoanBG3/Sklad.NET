using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Sklad.Controllers;

public class CultureController : Controller
{
    public IActionResult Set(string culture, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });
        }
        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }
}
