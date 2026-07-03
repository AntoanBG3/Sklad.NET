using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sklad.Data;

namespace Sklad.Tests;

public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SkladDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SkladDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new SkladDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
