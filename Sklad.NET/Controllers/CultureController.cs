using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Sklad.Controllers;

[AllowAnonymous]
public class CultureController : Controller
{
    private readonly RequestLocalizationOptions _localizationOptions;

    public CultureController(IOptions<RequestLocalizationOptions> localizationOptions)
        => _localizationOptions = localizationOptions.Value;

    public IActionResult Set(string culture, string? returnUrl)
    {
        var supported = _localizationOptions.SupportedUICultures?
            .Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase)) == true;

        if (supported)
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });
        }
        return LocalRedirect(Sklad.Helpers.Redirects.Safe(returnUrl));
    }
}
