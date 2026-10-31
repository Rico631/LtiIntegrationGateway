using System.Security.Cryptography.X509Certificates;
using System.Text;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>Сбор сообщений <see cref="InboundMessage"/>, доставленных продукту через шину.</summary>
public sealed class CollectingInboundHandler : IHandleMessages<InboundMessage>
{
    private readonly List<InboundMessage> _messages = [];

    public IReadOnlyList<InboundMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }

    public Task Handle(InboundMessage message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<InboundMessage>> WaitForCountAsync(int expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var messages = Messages;

            if (messages.Count >= expected)
            {
                return messages;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"не дождались {expected} сообщений: получено {Messages.Count}");
    }
}

/// <summary>Публикатор, воспроизводящий недоступность шины.</summary>
public sealed class FailingInboundMessagePublisher : IInboundMessagePublisher
{
    public Task PublishAsync(
        string recipientQueue,
        InboundMessage message,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("шина недоступна");
}

/// <summary>
/// Тестовый контур приёма: SQLite в памяти, тестовое хранилище секретов и два in-memory endpoint'а
/// Rebus на одной сети — шлюз и продукт-получатель.
/// </summary>
public sealed class InboundFixture : IAsyncDisposable
{
    public const string ChannelName = "bank-a";
    public const string RecipientQueue = "dpv.inbox";

    public static readonly Guid ContractVersionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly InMemNetwork _network = new();
    private readonly IBus _productBus;

    private readonly InboundSchemaCache _schemaCache = new();

    private ServiceProvider _services;
    private IBus _gatewayBus;

    public InboundFixture()
    {
        Database = new SqliteInMemoryDatabase();
        Database.Migrate();

        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        var productActivator = new BuiltinHandlerActivator();
        productActivator.Register(() => Received);

        _productBus = Configure.With(productActivator)
            .Transport(transport => transport.UseInMemoryTransport(_network, RecipientQueue))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        var gatewayActivator = new BuiltinHandlerActivator();

        _gatewayBus = Configure.With(gatewayActivator)
            .Transport(transport => transport.UseInMemoryTransport(_network, "gateway.inbound"))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        _services = BuildServices(new RebusInboundMessagePublisher(_gatewayBus));
    }

    private ServiceProvider BuildServices(IInboundMessagePublisher publisher)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<ISecretStore>(Secrets);
        services.AddSingleton(publisher);
        services.AddSingleton(_schemaCache);
        services.AddSingleton<InboundRequestAuthenticator>();
        services.AddSingleton<InboundMessageLogWriter>();
        services.AddSingleton<InboundRequestHandler>();
        services.AddScoped(_ => Database.CreateContext());

        return services.BuildServiceProvider();
    }

    public SqliteInMemoryDatabase Database { get; }

    public CollectingInboundHandler Received { get; } = new();

    public TestSecretStore Secrets { get; } = new();

    public IServiceProvider Services => _services;

    public InboundRequestHandler Handler => _services.GetRequiredService<InboundRequestHandler>();

    public InboundSchemaCache SchemaCache => _schemaCache;

    /// <summary>Прогревает кэш схем так же, как это делает активация версии контракта.</summary>
    public Task WarmUpAsync(
        string httpMethod,
        string template,
        string requestSchemas,
        Guid? versionId = null) =>
        SchemaCache.WarmUpAsync(
            versionId ?? ContractVersionId,
            [
                new ContractOperation
                {
                    Id = Guid.NewGuid(),
                    ContractVersionId = versionId ?? ContractVersionId,
                    HttpMethod = httpMethod,
                    Template = template,
                    RequestSchemas = requestSchemas,
                },
            ]);

    public InboundRouteMetadata Route(
        ChannelAuthType authType = ChannelAuthType.None,
        string? configKey = null,
        string? headerName = null,
        string? userName = null,
        string? recipientQueue = RecipientQueue,
        string? operationId = "receiveContract",
        Guid? versionId = null,
        int? rateLimitPermit = null,
        int? rateLimitWindowSeconds = null) =>
        new(
            ChannelName,
            versionId ?? ContractVersionId,
            operationId,
            recipientQueue,
            authType,
            configKey,
            headerName,
            userName,
            rateLimitPermit,
            rateLimitWindowSeconds);

    /// <summary>Выполняет входящий вызов так, как его получил бы динамический маршрут.</summary>
    public async Task<int> InvokeAsync(
        InboundRouteMetadata route,
        string method = "POST",
        string path = "/callbacks/contracts",
        string? template = "/callbacks/contracts",
        string? body = null,
        string? contentType = "application/json",
        IReadOnlyDictionary<string, string>? headers = null,
        string? queryString = null,
        X509Certificate2? clientCertificate = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _services,
        };

        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = contentType;

        if (!string.IsNullOrEmpty(queryString))
        {
            context.Request.QueryString = new QueryString(queryString);
        }

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                context.Request.Headers[header.Key] = header.Value;
            }
        }

        var bytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;

        if (clientCertificate is not null)
        {
            context.Connection.ClientCertificate = clientCertificate;
        }

        context.SetEndpoint(new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse(string.IsNullOrEmpty(template) ? path : template),
            order: 0,
            new EndpointMetadataCollection(route),
            displayName: $"inbound:{template}"));

        await Handler.HandleAsync(context);

        return context.Response.StatusCode;
    }

    /// <summary>Имитирует недоступность шины: последующие вызовы завершаются сбоем передачи.</summary>
    public async Task BreakGatewayBusAsync()
    {
        var previous = _services;

        _services = BuildServices(new FailingInboundMessagePublisher());

        await previous.DisposeAsync();
    }

    public async Task<IReadOnlyList<InboundMessageLog>> LogsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        return (await db.InboundMessageLogs
                .AsNoTracking()
                .ToArrayAsync())
            .OrderBy(entry => entry.ReceivedAt)
            .ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _gatewayBus.Dispose();
        _productBus.Dispose();
        await _services.DisposeAsync();
        Database.Dispose();
    }
}
