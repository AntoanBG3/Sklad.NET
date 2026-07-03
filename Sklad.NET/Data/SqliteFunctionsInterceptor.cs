using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sklad.Data;

/// <summary>
/// SQLite's built-in lower()/LIKE only fold ASCII case, so Cyrillic search would
/// stay case-sensitive. Registers a .NET-backed unilower() on every connection;
/// SkladDbContext.UniLower maps to it.
/// </summary>
public class SqliteFunctionsInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    public static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
            sqlite.CreateFunction("unilower", (string? s) => s?.ToLowerInvariant(), isDeterministic: true);
    }
}
