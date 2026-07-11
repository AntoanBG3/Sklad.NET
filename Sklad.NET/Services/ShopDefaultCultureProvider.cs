using Microsoft.AspNetCore.Localization;

namespace Sklad.Services;

// Registered after the query-string and cookie providers, so it only decides
// for visitors who have expressed no culture choice yet. An unsupported value
// is rejected by the middleware's supported-culture check and falls through to
// DefaultRequestCulture.
public class ShopDefaultCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var cache = httpContext.RequestServices.GetRequiredService<DefaultCultureCache>();
        if (!cache.TryGet(out var culture))
        {
            var settings = httpContext.RequestServices.GetRequiredService<IShopSettingsService>();
            culture = (await settings.GetAsync()).DefaultCulture;
            cache.Set(culture);
        }
        return culture is null ? null : new ProviderCultureResult(culture);
    }
}
