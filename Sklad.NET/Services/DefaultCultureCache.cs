namespace Sklad.Services;

// UseRequestLocalization runs for every request, static assets included, so the
// shop's default culture cannot cost a database read each time. The provider
// fills this once on first use and SaveAsync overwrites it on every save.
public class DefaultCultureCache
{
    private volatile bool _loaded;
    private volatile string? _culture;

    public bool TryGet(out string? culture)
    {
        culture = _culture;
        return _loaded;
    }

    public void Set(string? culture)
    {
        _culture = culture;
        _loaded = true;
    }
}
