using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sklad.Data;

/// <summary>
/// SQLite's built-in lower()/LIKE/NOCASE only fold ASCII case, so Cyrillic text
/// would stay case-sensitive. Registers .NET-backed Unicode lowering and
/// collation on every connection; SkladDbContext maps identifier columns to them.
/// </summary>
public class SqliteFunctionsInterceptor : DbConnectionInterceptor
{
    public const string UnicodeNoCaseCollation = "UNICODE_NOCASE";

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
        {
            sqlite.CreateFunction("unilower", (string? s) => s?.ToLowerInvariant(), isDeterministic: true);
            sqlite.CreateCollation(UnicodeNoCaseCollation, StringComparer.OrdinalIgnoreCase.Compare);
        }
    }
}
