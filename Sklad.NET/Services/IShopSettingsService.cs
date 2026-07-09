using Sklad.Models;

namespace Sklad.Services;

public interface IShopSettingsService
{
    Task<ShopSettings> GetAsync();

    Task SaveAsync(ShopSettings settings);
}
