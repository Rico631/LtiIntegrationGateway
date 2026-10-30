using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LtiIntegrationGateway.Infrastructure.Persistence;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// База SQLite в режиме <c>Data Source=:memory:</c>.
/// </summary>
/// <remarks>
/// Соединение остаётся открытым на всё время жизни объекта: при закрытии соединения
/// in-memory база уничтожается, поэтому между операциями его нельзя отпускать.
/// </remarks>
public sealed class SqliteInMemoryDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public DbContextOptions<GatewayDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite(_connection)
            .Options;

    public GatewayDbContext CreateContext() => new(CreateOptions());

    /// <summary>Применяет миграции, чтобы тестовая схема совпадала с приложением.</summary>
    public void Migrate()
    {
        using var context = CreateContext();
        context.Database.Migrate();
    }

    public async Task<long> CountAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt64(result);
    }

    public void Dispose() => _connection.Dispose();
}
