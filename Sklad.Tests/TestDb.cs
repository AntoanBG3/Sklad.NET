using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sklad.Data;

namespace Sklad.Tests;

public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        // The app registers unilower via SqliteFunctionsInterceptor on open; this
        // connection is already open, so register directly.
        SqliteFunctionsInterceptor.Register(_connection);
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SkladDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<SkladDbContext>().UseSqlite(_connection);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new SkladDbContext(builder.Options);
    }

    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
