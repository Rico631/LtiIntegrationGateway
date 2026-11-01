using System.Globalization;
using LtiIntegrationGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Отдельная база PostgreSQL на тест поверх контейнера, общего для всего прогона.
/// </summary>
/// <remarks>
/// Контейнер поднимается один раз, схема один раз накатывается в шаблонную базу, а каждый тест
/// получает клон шаблона. Тест видит ту же схему, что и промышленный запуск: частичные уникальные
/// индексы, timestamptz и блокировки строк работают так же, как в эксплуатации.
/// </remarks>
public sealed class PostgresTestDatabase : IDisposable
{
    private const string TemplateName = "gateway_template";

    private static readonly Lazy<string> Template = new(() =>
    {
        Execute($"CREATE DATABASE \"{TemplateName}\"");

        using (var context = new GatewayDbContext(
                   new DbContextOptionsBuilder<GatewayDbContext>()
                       .UseNpgsql(ConnectionStringFor(TemplateName))
                       .Options))
        {
            context.Database.Migrate();
        }

        // Клон шаблона невозможен, пока к нему есть подключения.
        NpgsqlConnection.ClearAllPools();

        return TemplateName;
    });

    private readonly string _databaseName;

    public PostgresTestDatabase()
    {
        _databaseName = $"gateway_tests_{Guid.NewGuid():N}";
        Execute($"CREATE DATABASE \"{_databaseName}\" TEMPLATE \"{Template.Value}\"");

        ConnectionString = ConnectionStringFor(_databaseName);
    }

    public string ConnectionString { get; }

    public DbContextOptions<GatewayDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql(ConnectionString)
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        // Закрывается только пул этой базы: ClearAllPools задел бы параллельно идущие тесты.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        Execute($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private static void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(Container.Value.GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ConnectionStringFor(string databaseName) =>
        new NpgsqlConnectionStringBuilder(Container.Value.GetConnectionString())
        {
            Database = databaseName,
            // Много параллельных тестовых баз: пул на базу должен быть небольшим.
            MaxPoolSize = 5,
        }.ConnectionString;

    private static readonly Lazy<PostgreSqlContainer> Container = new(() =>
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        container.StartAsync().GetAwaiter().GetResult();

        return container;
    });
}
