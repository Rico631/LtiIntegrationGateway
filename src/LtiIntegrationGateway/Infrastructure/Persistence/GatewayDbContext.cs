using System.Text.Json;
using System.Text.Json.Serialization;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Contracts;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Inbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Management;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Outbound;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.ProductRoutes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Контекст хранилища конфигурации шлюза.
/// </summary>
/// <remarks>
/// Содержит модель конфигурации каналов. Сущности других срезов добавляются вместе с этими
/// срезами, а не заранее.
/// </remarks>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions RetryPolicyJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly ValueConverter<RetryPolicy, string> RetryPolicyConverter = new(
        policy => JsonSerializer.Serialize(policy, RetryPolicyJsonOptions),
        json => DeserializeRetryPolicy(json));

    private static readonly ValueComparer<RetryPolicy> RetryPolicyComparer = new(
        (left, right) => SerializeRetryPolicy(left) == SerializeRetryPolicy(right),
        policy => SerializeRetryPolicy(policy).GetHashCode(StringComparison.Ordinal),
        policy => DeserializeRetryPolicy(SerializeRetryPolicy(policy)));

    /// <summary>
    /// Хранит момент времени как миллисекунды Unix: SQLite не умеет сортировать DateTimeOffset,
    /// а целочисленная колонка сохраняет порядок и участвует в индексах.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> UnixMillisecondsConverter = new(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    public DbSet<OutboundChannel> OutboundChannels => Set<OutboundChannel>();

    public DbSet<InboundChannel> InboundChannels => Set<InboundChannel>();

    public DbSet<ChannelRevision> ChannelRevisions => Set<ChannelRevision>();

    public DbSet<ChannelAuditEntry> ChannelAuditEntries => Set<ChannelAuditEntry>();

    public DbSet<ProductRoute> ProductRoutes => Set<ProductRoute>();

    public DbSet<ProductRouteChannel> ProductRouteChannels => Set<ProductRouteChannel>();

    public DbSet<ProductRouteQueueAddress> ProductRouteQueueAddresses => Set<ProductRouteQueueAddress>();

    public DbSet<OutboundOperation> OutboundOperations => Set<OutboundOperation>();

    public DbSet<OutboundMessageLog> OutboundMessageLogs => Set<OutboundMessageLog>();

    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();

    public DbSet<ContractOperation> ContractOperations => Set<ContractOperation>();

    public DbSet<ManagementAuditEntry> ManagementAuditEntries => Set<ManagementAuditEntry>();

    public DbSet<InboundMessageLog> InboundMessageLogs => Set<InboundMessageLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboundChannel>(channel =>
        {
            channel.HasKey(entity => entity.Id);
            channel.Property(entity => entity.Name).HasMaxLength(64).IsRequired();
            channel.HasIndex(entity => entity.Name).IsUnique();

            channel
                .HasMany(entity => entity.Revisions)
                .WithOne(revision => revision.OutboundChannel)
                .HasForeignKey(revision => revision.OutboundChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InboundChannel>(channel =>
        {
            channel.HasKey(entity => entity.Id);
            channel.Property(entity => entity.Name).HasMaxLength(64).IsRequired();
            channel.HasIndex(entity => entity.Name).IsUnique();
            channel.Property(entity => entity.ResponseMode).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<ChannelRevision>(revision =>
        {
            revision.HasKey(entity => entity.Id);
            revision.Property(entity => entity.BaseUrl).HasMaxLength(2048).IsRequired();
            revision.Property(entity => entity.ConfigKey).HasMaxLength(256);
            revision.Property(entity => entity.HeaderName).HasMaxLength(128);
            revision.Property(entity => entity.UserName).HasMaxLength(256);
            revision.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(32);
            revision.Property(entity => entity.AuthType).HasConversion<string>().HasMaxLength(32);

            var retryPolicy = revision
                .Property(entity => entity.RetryPolicy)
                .HasConversion(RetryPolicyConverter)
                .HasColumnName("RetryPolicyJson");

            retryPolicy.Metadata.SetValueComparer(RetryPolicyComparer);

            revision.HasIndex(entity => new { entity.OutboundChannelId, entity.Number }).IsUnique();
            revision.HasIndex(entity => new { entity.OutboundChannelId, entity.Status });
        });

        modelBuilder.Entity<ChannelAuditEntry>(audit =>
        {
            audit.HasKey(entity => entity.Id);
            audit.Property(entity => entity.Operation).HasMaxLength(64).IsRequired();
            audit.Property(entity => entity.Direction).HasConversion<string>().HasMaxLength(16);
            audit.Property(entity => entity.Details).HasMaxLength(2048);
            audit.HasIndex(entity => new { entity.Direction, entity.ChannelId, entity.OccurredAt });
        });

        modelBuilder.Entity<ProductRoute>(route =>
        {
            route.HasKey(entity => entity.Id);
            route.Property(entity => entity.ProductId).HasMaxLength(32).IsRequired();
            route.HasIndex(entity => entity.ProductId).IsUnique();

            route
                .HasMany(entity => entity.AllowedChannels)
                .WithOne(channel => channel.ProductRoute)
                .HasForeignKey(channel => channel.ProductRouteId)
                .OnDelete(DeleteBehavior.Cascade);

            route
                .HasMany(entity => entity.AllowedQueueAddresses)
                .WithOne(address => address.ProductRoute)
                .HasForeignKey(address => address.ProductRouteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductRouteChannel>(channel =>
        {
            channel.HasKey(entity => entity.Id);
            channel.Property(entity => entity.ChannelName).HasMaxLength(64).IsRequired();
            channel.HasIndex(entity => new { entity.ProductRouteId, entity.ChannelName }).IsUnique();
        });

        modelBuilder.Entity<ProductRouteQueueAddress>(address =>
        {
            address.HasKey(entity => entity.Id);
            address.Property(entity => entity.Address).HasMaxLength(256).IsRequired();
            address.HasIndex(entity => new { entity.ProductRouteId, entity.Address }).IsUnique();
        });

        modelBuilder.Entity<OutboundOperation>(operation =>
        {
            operation.HasKey(entity => entity.Id);
            operation.Property(entity => entity.MessageId).HasMaxLength(128).IsRequired();
            operation.HasIndex(entity => entity.MessageId).IsUnique();
            operation.Property(entity => entity.ProductId).HasMaxLength(32).IsRequired();
            operation.Property(entity => entity.ChannelName).HasMaxLength(64).IsRequired();
            operation.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(32);
            operation.Property(entity => entity.Outcome).HasMaxLength(32);
            operation.Property(entity => entity.ErrorCode).HasMaxLength(64);
            operation.Property(entity => entity.CorrelationId).HasMaxLength(128);
            operation.Property(entity => entity.ReturnAddress).HasMaxLength(256);
        });

        modelBuilder.Entity<OutboundMessageLog>(log =>
        {
            log.HasKey(entity => entity.Id);
            log.Property(entity => entity.MessageId).HasMaxLength(128).IsRequired();
            log.Property(entity => entity.ProductId).HasMaxLength(32).IsRequired();
            log.Property(entity => entity.ChannelName).HasMaxLength(64).IsRequired();
            log.Property(entity => entity.HttpMethod).HasMaxLength(16).IsRequired();
            log.Property(entity => entity.RelativePath).HasMaxLength(2048).IsRequired();
            log.Property(entity => entity.ContentType).HasMaxLength(256);
            log.Property(entity => entity.Outcome).HasMaxLength(32);
            log.Property(entity => entity.ErrorCode).HasMaxLength(64);
            log.Property(entity => entity.ErrorMessage).HasMaxLength(2048);
            log.Property(entity => entity.DeliveryAddress).HasMaxLength(256).IsRequired();
            log.Property(entity => entity.DeliveryStatus).HasConversion<string>().HasMaxLength(16);
            log.Property(entity => entity.CreatedAt).HasConversion(UnixMillisecondsConverter);
            log.HasIndex(entity => new { entity.MessageId, entity.DeliveryStatus });
            // Индекс для диспетчера: выборка недоставленных записей в порядке создания без
            // полного сканирования (CreatedAt хранится как миллисекунды Unix).
            log.HasIndex(entity => new { entity.DeliveryStatus, entity.CreatedAt });
            // Индекс для выборки последних ошибок канала: здоровье канала читает журнал по каналу
            // в порядке времени.
            log.HasIndex(entity => new { entity.ChannelName, entity.CreatedAt });
        });

        modelBuilder.Entity<ContractVersion>(version =>
        {
            version.HasKey(entity => entity.Id);
            version.Property(entity => entity.SourceFormat).HasConversion<string>().HasMaxLength(16);
            version.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(16);
            version.Property(entity => entity.Document).IsRequired();
            version.Property(entity => entity.RecipientQueue).HasMaxLength(256);
            version.Property(entity => entity.AuthType).HasConversion<string>().HasMaxLength(32);
            version.Property(entity => entity.ConfigKey).HasMaxLength(256);
            version.Property(entity => entity.HeaderName).HasMaxLength(128);
            version.Property(entity => entity.UserName).HasMaxLength(128);
            version.HasIndex(entity => new { entity.InboundChannelId, entity.Version }).IsUnique();
            version.HasIndex(entity => new { entity.InboundChannelId, entity.Status });

            version
                .HasOne(entity => entity.InboundChannel)
                .WithMany(channel => channel.ContractVersions)
                .HasForeignKey(entity => entity.InboundChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            version
                .HasMany(entity => entity.Operations)
                .WithOne(operation => operation.ContractVersion)
                .HasForeignKey(operation => operation.ContractVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContractOperation>(operation =>
        {
            operation.HasKey(entity => entity.Id);
            operation.Property(entity => entity.HttpMethod).HasMaxLength(16).IsRequired();
            operation.Property(entity => entity.Template).HasMaxLength(2048).IsRequired();
            operation.Property(entity => entity.OperationId).HasMaxLength(256);
            operation.Property(entity => entity.Enabled).HasDefaultValue(true).HasSentinel(true);
            operation.Property(entity => entity.RecipientQueue).HasMaxLength(256);
            operation.HasIndex(entity => new { entity.ContractVersionId, entity.HttpMethod, entity.Template }).IsUnique();
        });

        modelBuilder.Entity<ManagementAuditEntry>(audit =>
        {
            audit.HasKey(entity => entity.Id);
            audit.Property(entity => entity.Operation).HasMaxLength(64).IsRequired();
            audit.Property(entity => entity.Target).HasMaxLength(256).IsRequired();
            audit.Property(entity => entity.Result).HasMaxLength(16).IsRequired();
            audit.Property(entity => entity.Details).HasMaxLength(2048);
            audit.HasIndex(entity => entity.Operation);
        });

        modelBuilder.Entity<InboundMessageLog>(log =>
        {
            log.HasKey(entity => entity.Id);
            log.Property(entity => entity.MessageId).HasMaxLength(128).IsRequired();
            log.Property(entity => entity.ChannelName).HasMaxLength(64).IsRequired();
            log.Property(entity => entity.HttpMethod).HasMaxLength(16).IsRequired();
            log.Property(entity => entity.RelativePath).HasMaxLength(2048).IsRequired();
            log.Property(entity => entity.RouteTemplate).HasMaxLength(2048);
            log.Property(entity => entity.OperationId).HasMaxLength(256);
            log.Property(entity => entity.ContentType).HasMaxLength(256);
            log.Property(entity => entity.QueryParameters).HasMaxLength(4096);
            log.Property(entity => entity.DeliveryStatus).HasConversion<string>().HasMaxLength(16);
            log.Property(entity => entity.ErrorCode).HasMaxLength(64);
            log.Property(entity => entity.ResponseContentType).HasMaxLength(256);
            log.Property(entity => entity.ResponseHeaders).HasMaxLength(8192);
            log.Property(entity => entity.ReceivedAt).HasConversion(UnixMillisecondsConverter);
            log.HasIndex(entity => entity.MessageId);
            // Индекс для выборки последних ошибок канала: здоровье канала читает журнал по каналу
            // в порядке времени.
            log.HasIndex(entity => new { entity.ChannelName, entity.ReceivedAt });
        });
    }

    private static string SerializeRetryPolicy(RetryPolicy? policy) =>
        JsonSerializer.Serialize(policy ?? RetryPolicy.Empty, RetryPolicyJsonOptions);

    private static RetryPolicy DeserializeRetryPolicy(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? RetryPolicy.Empty
            : JsonSerializer.Deserialize<RetryPolicy>(json, RetryPolicyJsonOptions) ?? RetryPolicy.Empty;
}

