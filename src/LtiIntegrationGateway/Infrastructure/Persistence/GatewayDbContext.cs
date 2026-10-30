using Microsoft.EntityFrameworkCore;

namespace LtiIntegrationGateway.Infrastructure.Persistence;

/// <summary>
/// Контекст хранилища конфигурации шлюза.
/// </summary>
/// <remarks>
/// Сущности добавляются только тем вертикальным срезом, который их использует: контекст
/// существует ради регистрации провайдера и проверки строки подключения, а не «на будущее».
/// </remarks>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options);
