using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Регистрация хранилища PostgreSQL для обоих сервисов: обязательная строка подключения, проверка
/// базы и ограниченный пул соединений.
/// </summary>
/// <remarks>
/// Строка подключения читается из ключа <c>ConnectionStrings:Gateway</c>. Пул по умолчанию ограничен,
/// потому что с одной базой работают management в двух репликах и по два экземпляра flow на интеграцию;
/// явно заданный в строке размер пула не переопределяется.
/// </remarks>
public static class GatewayStorageExtensions
{
    /// <summary>Имя строки подключения в конфигурации.</summary>
    public const string ConnectionStringName = "Gateway";

    /// <summary>Сообщение об отсутствующей строке подключения; содержит имя ключа без значения.</summary>
    public const string MissingConnectionStringMessage =
        "Строка подключения к хранилищу не настроена. Задайте ключ конфигурации 'ConnectionStrings:Gateway'.";

    /// <summary>Размер пула по умолчанию, если строка подключения его не задаёт.</summary>
    public const int DefaultMaximumPoolSize = 20;

    /// <summary>
    /// Диагностическое сообщение при отказе аутентификации PostgreSQL. Не содержит значения пароля;
    /// объясняет типовую причину локального запуска через AppHost.
    /// </summary>
    public const string AuthenticationFailureMessage =
        "Не удалось подключиться к PostgreSQL: неверный пользователь или пароль в ключе " +
        "'ConnectionStrings:Gateway'. При локальном запуске через AppHost удалите том " +
        "'lti-gateway-postgres-data' (docker volume rm lti-gateway-postgres-data): том сохраняет " +
        "пароль первого запуска, а AppHost использует фиксированный пароль разработки.";

    /// <summary>Добавляет <see cref="GatewayDbContext"/> поверх PostgreSQL.</summary>
    /// <remarks>
    /// Строка подключения читается при создании контекста, а не при регистрации: тестовый хост
    /// подменяет конфигурацию после выполнения точки входа.
    /// </remarks>
    public static IServiceCollection AddGatewayStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<GatewayDbContext>((provider, options) =>
        {
            var current = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(MissingConnectionStringMessage);

            options.UseNpgsql(CreateBuilder(current).ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Признак отказа аутентификации PostgreSQL (SQLSTATE 28P01/28000) с учётом обёрток EF/Npgsql.
    /// </summary>
    public static bool IsAuthenticationFailure(Exception? exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } => true,
        PostgresException { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } => true,
        NpgsqlException { InnerException: { } inner } => IsAuthenticationFailure(inner),
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsAuthenticationFailure),
        _ => false,
    };

    /// <summary>
    /// Проверяет строку подключения и дополняет её параметрами пула. Ошибка содержит имя
    /// некорректного параметра и не раскрывает значение пароля.
    /// </summary>
    public static NpgsqlConnectionStringBuilder CreateBuilder(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Ключ конфигурации 'ConnectionStrings:Gateway' содержит некорректную строку подключения.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException(
                "Ключ конфигурации 'ConnectionStrings:Gateway' не задаёт имя базы данных.");
        }

        if (!builder.ContainsKey("Maximum Pool Size") && !builder.ContainsKey("Max Pool Size"))
        {
            builder.MaxPoolSize = DefaultMaximumPoolSize;
        }

        return builder;
    }
}
