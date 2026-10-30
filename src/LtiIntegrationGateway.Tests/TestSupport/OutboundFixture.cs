using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Channels;
using LtiIntegrationGateway.Features.Outbound;
using LtiIntegrationGateway.Features.ProductRoutes;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Security;
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
/// Тестовый контур исходящего среза: SQLite в памяти, тестовый HTTP и два in-memory endpoint'а Rebus
/// на одной сети — шлюз и продукт.
/// </summary>
public sealed class OutboundFixture : IAsyncDisposable
{
    public const string ProductId = "dpv";
    public const string ChannelName = "bank-a";
    public const string ProductInbox = "product.inbox";

    private readonly IBus _gatewayBus;
    private readonly IBus _productBus;

    public OutboundFixture()
    {
        Database = new SqliteInMemoryDatabase();
        Database.Migrate();

        var network = new InMemNetwork();
        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        var productActivator = new BuiltinHandlerActivator();
        productActivator.Register(() => Results);

        _productBus = Configure.With(productActivator)
            .Transport(transport => transport.UseInMemoryTransport(network, ProductInbox))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        _gatewayBus = Configure.With(new BuiltinHandlerActivator())
            .Transport(transport => transport.UseInMemoryTransport(network, RebusConfigurationExtensions.OutboundQueue))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();
    }

    public SqliteInMemoryDatabase Database { get; }

    public RecordingHttpMessageHandler Http { get; } = new();

    public CollectingResultHandler Results { get; } = new();

    public TestSecretStore Secrets { get; } = new();

    public GatewayDbContext CreateDb() => Database.CreateContext();

    public async Task<Guid> CreateActiveChannelAsync(
        RetryPolicy? retryPolicy = null,
        ChannelAuthType authType = ChannelAuthType.None,
        string? configKey = null,
        string? headerName = "X-Api-Key",
        IReadOnlyList<string>? allowedMethods = null,
        IReadOnlyList<string>? allowedPaths = null,
        int perAttemptTimeoutMs = 5000,
        int overallCapMs = 30000)
    {
        using var db = CreateDb();

        var configuration = new ChannelConfiguration(
            "https://bank-a.example",
            authType,
            configKey,
            headerName,
            UserName: null,
            perAttemptTimeoutMs,
            overallCapMs,
            retryPolicy ?? new RetryPolicy([RetryRule.TransportError([1, 1])]),
            allowedMethods ?? ["GET", "POST"],
            allowedPaths ?? ["/api/contracts", "/api/contracts/{id}"]);

        var created = await new CreateChannelHandler(db).HandleAsync(new CreateChannelCommand(ChannelName, configuration));

        await new PublishChannelRevisionHandler(db, new ChannelConfigurationValidator())
            .HandleAsync(new PublishChannelRevisionCommand(created.ChannelId!.Value, 1));

        await new ActivateChannelRevisionHandler(db)
            .HandleAsync(new ActivateChannelRevisionCommand(created.ChannelId!.Value, 1));

        return created.ChannelId!.Value;
    }

    public async Task RegisterProductAsync()
    {
        using var db = CreateDb();

        await new RegisterProductRouteHandler(db).HandleAsync(new RegisterProductRouteCommand(
            ProductId,
            [ChannelName],
            [ProductInbox]));
    }

    public ProcessOutboundRequestHandler CreateProcessor(GatewayDbContext db) => new(
        new ResolveSenderIdentityHandler(db),
        new LoadActiveChannelHandler(db),
        new ResolveAuthorizationHandler(Secrets),
        new ExecuteHttpCallHandler(new StubHttpClientFactory(Http)),
        new OutboundOperationRecorder(db),
        new PublishOutboundResultHandler(_gatewayBus, new OutboundOperationRecorder(db)));

    public static OutboundRequest CreateRequest(
        string messageId = "msg-1",
        string method = "GET",
        string relativePath = "/api/contracts",
        string? idempotencyKey = null,
        byte[]? body = null,
        string? contentType = null)
    {
        var request = new OutboundRequest
        {
            MessageId = messageId,
            ProductId = ProductId,
            ChannelName = ChannelName,
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
        _gatewayBus.Dispose();
        _productBus.Dispose();
        Database.Dispose();

        return ValueTask.CompletedTask;
    }
}

