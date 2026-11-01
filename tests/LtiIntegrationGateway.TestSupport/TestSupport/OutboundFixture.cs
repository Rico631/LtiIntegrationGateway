using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Channels.ActivateChannelRevision;
using LtiIntegrationGateway.Features.Channels.CreateChannel;
using LtiIntegrationGateway.Features.Channels.PublishChannelRevision;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Хранилище секретов для тестов: значения задаются в самом тесте.</summary>
public sealed class TestSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public TestSecretStore With(string configKey, string value)
    {
        _values[configKey] = value;

        return this;
    }

    public string? Find(string configKey) => _values.GetValueOrDefault(configKey);
}

/// <summary>Сбор результатов, доставленных продукту через шину.</summary>
public sealed class CollectingResultHandler : IHandleMessages<OutboundResult>
{
    private readonly List<OutboundResult> _results = [];

    public IReadOnlyList<OutboundResult> Results
    {
        get
        {
            lock (_results)
            {
                return _results.ToArray();
            }
        }
    }

    public Task Handle(OutboundResult message)
    {
        lock (_results)
        {
            _results.Add(message);
        }

        return Task.CompletedTask;
    }

    public async Task<OutboundResult> WaitForSingleAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Results.Count > 0)
            {
                return Results[0];
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("результат не был доставлен продукту");
    }

    public async Task<IReadOnlyList<OutboundResult>> WaitForCountAsync(int expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var results = Results;

            if (results.Count >= expected)
            {
                return results;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"не дождались {expected} результатов");
    }
}
/// <summary>
/// Тестовый контур исходящего среза: PostgreSQL в контейнере, тестовый HTTP и два in-memory endpoint'а Rebus
/// на одной сети — шлюз и продукт.
/// </summary>
public sealed class OutboundFixture : IAsyncDisposable
{
    public const string ProductId = "dpv";
    public const string ChannelName = "bank-a";

    /// <summary>Вычисленный адрес очереди продукта: <c>product_id</c> + <c>.inbox</c> (ADR §10.2).</summary>
    public const string ProductInbox = "dpv.inbox";

    /// <summary>Интеграция тестового контура: одна на процесс, как в эксплуатации.</summary>
    public const string IntegrationId = IntegrationDefaults.Default;

    private readonly IBus _gatewayBus;
    private readonly IBus _productBus;
    private readonly IOptions<FlowOptions> _flowOptions =
        Microsoft.Extensions.Options.Options.Create(new FlowOptions { Integration = IntegrationId, InstanceId = "test" });
    private readonly List<ServiceProvider> _providers = [];

    public OutboundFixture()
    {
        Database = new PostgresTestDatabase();
        Database.Migrate();

        var network = new InMemNetwork();
        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        var productActivator = new BuiltinHandlerActivator();
        productActivator.Register(() => Results);

        _productBus = Configure.With(productActivator)
            .Transport(transport => transport.UseInMemoryTransport(network, QueueNames.ProductInbox(ProductId)))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        _gatewayBus = Configure.With(new BuiltinHandlerActivator())
            .Transport(transport => transport.UseInMemoryTransport(network, QueueNames.OutboundQueue(IntegrationId)))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        ChannelClients = new RecordingChannelHttpClientFactory(Http);
    }

    public PostgresTestDatabase Database { get; }

    public RecordingHttpMessageHandler Http { get; } = new();

    public RecordingChannelHttpClientFactory ChannelClients { get; }

    public CollectingResultHandler Results { get; } = new();

    public GatewayMetrics Metrics { get; } = new();

    public TestSecretStore Secrets { get; } = new();

    public TestChannelCertificateStore Certificates { get; } = new();

    public GatewayDbContext CreateDb() => Database.CreateContext();

    public async Task<Guid> CreateActiveChannelAsync(
        RetryPolicy? retryPolicy = null,
        ChannelAuthType authType = ChannelAuthType.None,
        string? configKey = null,
        string? headerName = "X-Api-Key",
        int perAttemptTimeoutMs = 5000,
        int overallCapMs = 30000,
        string channelName = ChannelName,
        string baseUrl = "https://bank-a.example")
    {
        using var db = CreateDb();

        var configuration = new ChannelConfiguration(
            baseUrl,
            authType,
            configKey,
            headerName,
            UserName: null,
            perAttemptTimeoutMs,
            overallCapMs,
            retryPolicy ?? new RetryPolicy([RetryRule.TransportError([1, 1])]));

        var created = await new CreateChannelHandler(db).HandleAsync(new CreateChannelCommand(channelName, configuration));

        await new PublishChannelRevisionHandler(
                db,
                new ChannelConfigurationValidator(),
                new TestSecretStore(),
                new TestChannelCertificateStore())
            .HandleAsync(new PublishChannelRevisionCommand(created.ChannelId!.Value, 1));

        await new ActivateChannelRevisionHandler(db)
            .HandleAsync(new ActivateChannelRevisionCommand(created.ChannelId!.Value, 1));

        return created.ChannelId!.Value;
    }

    public async Task RegisterProductAsync(
        IReadOnlyList<string>? channels = null,
        IReadOnlyList<string>? addresses = null)
    {
        using var db = CreateDb();

        // По умолчанию маршрут регистрируется без адресов: шлюз сохраняет вычисленный <product_id>.inbox.
        await new RegisterProductRouteHandler(db).HandleAsync(new RegisterProductRouteCommand(
            ProductId,
            channels ?? [ChannelName],
            addresses ?? []));
    }

    /// <summary>
    /// Создаёт активную ревизию канала другой интеграции: нужна для проверки изоляции — запрос
    /// экземпляра не должен использовать чужие каналы.
    /// </summary>
    public async Task CreateForeignChannelAsync(
        string integrationId,
        string channelName,
        string baseUrl = "https://foreign.example")
    {
        using var db = CreateDb();

        var channel = new OutboundChannel
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            Name = channelName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.OutboundChannels.Add(channel);
        db.ChannelRevisions.Add(new ChannelRevision
        {
            Id = Guid.NewGuid(),
            OutboundChannelId = channel.Id,
            Number = 1,
            Status = ChannelRevisionStatus.Active,
            BaseUrl = baseUrl,
            AuthType = ChannelAuthType.None,
            PerAttemptTimeoutMs = 5_000,
            OverallCapMs = 30_000,
            RetryPolicy = RetryPolicy.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Регистрирует маршрут продукта в указанной интеграции.</summary>
    public async Task RegisterProductForIntegrationAsync(
        string integrationId,
        IReadOnlyList<string> channels)
    {
        using var db = CreateDb();

        await new RegisterProductRouteHandler(db).HandleAsync(new RegisterProductRouteCommand(
            ProductId,
            channels,
            [],
            integrationId));
    }

    public ProcessOutboundRequestHandler CreateProcessor(GatewayDbContext db, IBus? bus = null) => new(
        new ResolveSenderIdentityHandler(db, _flowOptions),
        new LoadActiveChannelHandler(db, _flowOptions),
        new ResolveAuthorizationHandler(Secrets, Certificates),
        new ExecuteHttpCallHandler(ChannelClients),
        new OutboundOperationRecorder(db, _flowOptions),
        new StoredOutboundResultFactory(),
        new PublishOutboundResultHandler(bus ?? _gatewayBus, new OutboundOperationRecorder(db, _flowOptions)),
        Metrics);

    /// <summary>
    /// Собирает диспетчер с обычным для приложения жизненным циклом зависимостей: контекст БД на цикл,
    /// восстановление результата, отправка через <see cref="PublishOutboundResultHandler"/>.
    /// </summary>
    public OutboundResultDispatcher CreateDispatcher(
        IBus? bus = null,
        Action<OutboundDeliveryOptions>? configure = null,
        TimeProvider? timeProvider = null)
    {
        var options = new OutboundDeliveryOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Metrics);
        services.AddSingleton(bus ?? _gatewayBus);
        services.AddScoped(_ => CreateDb());
        services.AddScoped<OutboundOperationRecorder>();
        services.AddScoped<PublishOutboundResultHandler>();
        services.AddSingleton<StoredOutboundResultFactory>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton(_flowOptions);
        services.AddSingleton(timeProvider ?? TimeProvider.System);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return ActivatorUtilities.CreateInstance<OutboundResultDispatcher>(provider);
    }

    public static OutboundRequest CreateRequest(
        string messageId = "msg-1",
        string method = "GET",
        string relativePath = "/api/contracts",
        string? idempotencyKey = null,
        byte[]? body = null,
        string? contentType = null,
        string channelName = ChannelName)
    {
        var request = new OutboundRequest
        {
            MessageId = messageId,
            ProductId = ProductId,
            ChannelName = channelName,
            RelativePath = relativePath,
            HttpMethod = method,
            Body = Google.Protobuf.ByteString.CopyFrom(body ?? []),
        };

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.IdempotencyKey = idempotencyKey;
        }

        if (contentType is not null)
        {
            request.ContentType = contentType;
        }

        return request;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _gatewayBus.Dispose();
        _productBus.Dispose();
        Metrics.Dispose();
        Certificates.Dispose();
        Database.Dispose();

        return ValueTask.CompletedTask;
    }
}

