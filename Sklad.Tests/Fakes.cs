using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Sklad.Tests;

public sealed class NullTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

/// <summary>
/// No-op ObjectValidator so controller actions that call TryValidateModel can
/// run outside the MVC pipeline; tests assert service-level guards instead.
/// </summary>
public sealed class NoOpObjectValidator : Microsoft.AspNetCore.Mvc.ModelBinding.Validation.IObjectModelValidator
{
    public void Validate(Microsoft.AspNetCore.Mvc.ActionContext actionContext,
        Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidationStateDictionary? validationState,
        string prefix, object? model) { }
}

public class FakeLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments]
        => new(name, string.Format(name, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Simulates a competing writer: bumps every tire's Version through the shared
/// connection just before each SaveChanges, forcing a concurrency conflict.
/// </summary>
public sealed class BumpVersionsOnSave(SqliteConnection connection) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Bump();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Bump();
        return ValueTask.FromResult(result);
    }

    private void Bump()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Tires SET Version = Version + 1";
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// Simulates losing a duplicate-SKU race: inserts a row with the given SKU after
/// the service's pre-check but before its SaveChanges.
/// </summary>
public sealed class InsertDuplicateSkuOnSave(SqliteConnection connection, string sku) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Insert();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Insert();
        return ValueTask.FromResult(result);
    }

    private void Insert()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Tires (Sku, Brand, Model, Width, Profile, Diameter, Season, Type, UnitPrice, Quantity, MinStock, Version) " +
            "VALUES ($sku, 'Race', 'Winner', 205, 55, 16, 0, 0, '100.0', 1, 1, 0)";
        cmd.Parameters.AddWithValue("$sku", sku);
        cmd.ExecuteNonQuery();
    }
}
