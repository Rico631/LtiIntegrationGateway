using LtiIntegrationGateway.Infrastructure.Persistence;
using LtiIntegrationGateway.Infrastructure.Persistence.Entities.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LtiIntegrationGateway.Tests.TestSupport;

/// <summary>
/// Тестовый endpoint создаёт пустой входящий канал без контракта: сценарии реестра контрактов
/// затем загружают первую версию обычным management-вызовом.
/// </summary>
internal sealed class TestInboundChannelEndpointFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path == "/test/inbound-channels")
            {
                var name = context.Request.Query["name"].ToString();
                var db = context.RequestServices.GetRequiredService<GatewayDbContext>();

                if (!await db.InboundChannels.AnyAsync(channel => channel.Name == name))
                {
                    db.InboundChannels.Add(new InboundChannel
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        ResponseMode = InboundResponseMode.Async,
                        InboundResponseTimeoutMs = 30_000,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });

                    await db.SaveChangesAsync();
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            await nextMiddleware(context);
        });

        next(app);
    };
}
