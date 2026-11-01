using System.Security.Cryptography.X509Certificates;
using System.Text;
using LtiIntegrationGateway.Contracts.Messages.V1;
using LtiIntegrationGateway.Features.Configuration;
using LtiIntegrationGateway.Features.Inbound;
using LtiIntegrationGateway.Infrastructure.Observability;
using LtiIntegrationGateway.Infrastructure.OpenApi;
using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Infrastructure.Rebus;
using LtiIntegrationGateway.Infrastructure.Routing;
using LtiIntegrationGateway.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>Шина продукта, используемая для ответа в синхронном режиме.</summary>
    public IBus? ReplyBus { get; set; }

    /// <summary>Фабрика ответа; если задана и сообщение требует ответа, продукт отвечает.</summary>
    public Func<InboundMessage, InboundResponse?>? ReplyFactory { get; set; }

    /// <summary>Задержка ответа продукта; используется для проверки позднего ответа.</summary>
    public TimeSpan ReplyDelay { get; set; }

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

    public async Task Handle(InboundMessage message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        if (message.ReplyExpected && ReplyBus is not null && ReplyFactory?.Invoke(message) is { } response)
        {
            if (ReplyDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReplyDelay);
            }

            await ReplyBus.Reply(response);
        }
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

    public Task<InboundResponse> SendRequestAsync(
        string recipientQueue,
        InboundMessage message,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("шина недоступна");
}

/// <summary>Ответ тестового входящего вызова: статус, тело и заголовки.</summary>
public sealed record InboundTestResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string[]> Headers);


/// <summary>
/// Тестовый контур приёма: PostgreSQL в контейнере, тестовое хранилище секретов и два in-memory endpoint'а
/// Rebus на одной сети — шлюз и продукт-получатель.
/// </summary>
public sealed class InboundFixture : IAsyncDisposable
{
    public const string ChannelName = "bank-a";
    public const string RecipientQueue = "dpv.inbox";

    /// <summary>Интеграция экземпляра; совпадает с настройкой хоста по умолчанию.</summary>
    public const string IntegrationId = "default";

    /// <summary>Идентификатор экземпляра; определяет имя reply-очереди.</summary>
    public const string InstanceId = "test-instance";

    public static readonly Guid ContractVersionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly InMemNetwork _network = new();
    private readonly IBus _productBus;
    private readonly IBus _replyBus;
    private readonly PendingInboundRequests _pending = new();

    private readonly InboundSchemaCache _schemaCache = new();

    private ServiceProvider _services;
    private IBus _gatewayBus;

    public InboundFixture()
    {
        Database = new PostgresTestDatabase();
        Database.Migrate();

        var serializer = new ProtobufSerializer(new ProtobufMessageRegistry());

        var productActivator = new BuiltinHandlerActivator();
        productActivator.Register(() => Received);

        _productBus = Configure.With(productActivator)
            .Transport(transport => transport.UseInMemoryTransport(_network, RecipientQueue))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        Received.ReplyBus = _productBus;

        var gatewayActivator = new BuiltinHandlerActivator();

        _gatewayBus = Configure.With(gatewayActivator)
            .Transport(transport => transport.UseInMemoryTransport(_network, "gateway.inbound"))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        var replyActivator = new BuiltinHandlerActivator();

        replyActivator.Register(() => new InboundResponseHandler(
            _pending,
            NullLogger<InboundResponseHandler>.Instance));

        _replyBus = Configure.With(replyActivator)
            .Transport(transport => transport.UseInMemoryTransport(
                _network,
                QueueNames.ReplyQueue(IntegrationId, InstanceId)))
            .Serialization(serialization => serialization.Register(_ => serializer))
            .Start();

        _services = BuildServices(new RebusInboundMessagePublisher(
            _gatewayBus,
            Microsoft.Extensions.Options.Options.Create(new FlowOptions
            {
                Integration = IntegrationId,
                InstanceId = InstanceId,
            }),
            _pending));
    }

    private ServiceProvider BuildServices(IInboundMessagePublisher publisher)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<GatewayMetrics>();
        services.AddSingleton<ISecretStore>(Secrets);
        services.AddSingleton(publisher);
        services.AddSingleton(_schemaCache);
        services.AddSingleton<InboundRequestAuthenticator>();
        services.AddSingleton<InboundMessageLogWriter>();
        services.AddSingleton<InboundRequestHandler>();
        services.AddScoped(_ => Database.CreateContext());

        return services.BuildServiceProvider();
    }

    public PostgresTestDatabase Database { get; }

    public CollectingInboundHandler Received { get; } = new();

    public TestSecretStore Secrets { get; } = new();

    public IServiceProvider Services => _services;

    public GatewayMetrics Metrics => _services.GetRequiredService<GatewayMetrics>();

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
        int? rateLimitWindowSeconds = null,
        InboundResponseMode responseMode = InboundResponseMode.Async,
        int inboundResponseTimeoutMs = 30_000) =>
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
            rateLimitWindowSeconds,
            responseMode,
            inboundResponseTimeoutMs);

    /// <summary>Выполняет входящий вызов так, как его получил бы динамический маршрут.</summary>
    public async Task<InboundTestResponse> InvokeWithResponseAsync(
        InboundRouteMetadata route,
        string method = "POST",
        string path = "/callbacks/contracts",
        string? template = "/callbacks/contracts",
        string? body = null,
        string? contentType = "application/json",
        IReadOnlyDictionary<string, string>? headers = null,
        string? queryString = null,
        X509Certificate2? clientCertificate = null, CancellationToken requestAborted = default)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _services,
        };

        context.RequestAborted = requestAborted;

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

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await Handler.HandleAsync(context);

        return new InboundTestResponse(
            context.Response.StatusCode,
            Encoding.UTF8.GetString(responseBody.ToArray()),
            context.Response.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.Select(value => value ?? string.Empty).ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Выполняет входящий вызов и возвращает только HTTP-статус.</summary>
    public async Task<int> InvokeAsync(
        InboundRouteMetadata route,
        string method = "POST",
        string path = "/callbacks/contracts",
        string? template = "/callbacks/contracts",
        string? body = null,
        string? contentType = "application/json",
        IReadOnlyDictionary<string, string>? headers = null,
        string? queryString = null,
        X509Certificate2? clientCertificate = null,
        CancellationToken requestAborted = default) =>
        (await InvokeWithResponseAsync(
            route,
            method,
            path,
            template,
            body,
            contentType,
            headers,
            queryString,
            clientCertificate,
            requestAborted)).StatusCode;

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
        _replyBus.Dispose();
        _productBus.Dispose();
        await _services.DisposeAsync();
        Database.Dispose();
    }
}
