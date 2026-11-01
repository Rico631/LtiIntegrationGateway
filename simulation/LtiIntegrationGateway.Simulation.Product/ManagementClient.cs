using System.Text;
using System.Text.Json.Nodes;

namespace LtiIntegrationGateway.Simulation.Product;

/// <summary>
/// Регистрация конфигурации продукта в управляющем API: outbound-канал, два inbound-канала
/// с контрактами и маршрут продукта. Повторный запуск приводит конфигурацию к тому же виду.
/// </summary>
/// <remarks>
/// Все каналы создаются в интеграции <see cref="ProductSimulationOptions.Integration"/> — симуляция
/// использует bank-a, поэтому вызовы внешней системы обслуживает flow-bank-a.
/// </remarks>
public sealed class ManagementClient(HttpClient http, ProductSimulationOptions options)
{
    private string Base => $"/v1/integrations/{options.Integration}";

    /// <summary>Создаёт конфигурацию и ждёт, пока экземпляры интеграции её применят.</summary>
    public async Task EnsureConfigurationAsync(CancellationToken cancellationToken)
    {
        await EnsureOutboundChannelAsync(cancellationToken);
        await EnsureInboundChannelAsync(
            "product-orders-async",
            Contracts.AsyncOrders,
            "INBOUND_RESPONSE_MODE_ASYNC",
            cancellationToken);
        await EnsureInboundChannelAsync(
            "product-orders-sync",
            Contracts.SyncOrders,
            "INBOUND_RESPONSE_MODE_SYNC",
            cancellationToken);
        await EnsureProductRouteAsync(cancellationToken);
        await WaitUntilAppliedAsync(cancellationToken);
    }

    private async Task EnsureOutboundChannelAsync(CancellationToken cancellationToken)
    {
        var channels = await SendAsync(HttpMethod.Get, $"{Base}/outbound-channels", null, cancellationToken);

        if (Contains(channels?["channels"], ProductSimulationOptions.OutboundChannel))
        {
            var current = await SendAsync(
                HttpMethod.Get,
                $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}",
                null,
                cancellationToken);

            // Действующие настройки уже нужные: новая ревизия не создаётся, экземпляры не переприменяют конфигурацию.
            if (IsCurrentOutboundSettings(current?["channel"]?["activeSettings"]))
            {
                return;
            }

            var updated = await SendAsync(
                HttpMethod.Patch,
                $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}/draft",
                new JsonObject
                {
                    ["revision"] = 0,
                    ["settings"] = OutboundSettings(),
                },
                cancellationToken);

            AssertSucceeded(updated, "Обновление черновика outbound-канала");
        }
        else
        {
            var created = await SendAsync(
                HttpMethod.Post,
                $"{Base}/outbound-channels",
                new JsonObject
                {
                    ["name"] = ProductSimulationOptions.OutboundChannel,
                    ["settings"] = OutboundSettings(),
                },
                cancellationToken);

            AssertSucceeded(created, "Создание outbound-канала");
        }

        var channel = await SendAsync(
            HttpMethod.Get,
            $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}",
            null,
            cancellationToken);

        var draft = Draft(channel?["channel"]?["revisions"], "number")
            ?? throw new InvalidOperationException("У outbound-канала нет черновика ревизии.");

        var published = await SendAsync(
            HttpMethod.Post,
            $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}/revisions/{draft}/publish",
            null,
            cancellationToken);

        AssertSucceeded(published, "Публикация ревизии");

        var revision = published?["revision"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Ответ публикации не содержит номер ревизии.");

        var activated = await SendAsync(
            HttpMethod.Post,
            $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}/revisions/{revision}/activate",
            null,
            cancellationToken);

        AssertSucceeded(activated, "Активация ревизии");
    }

    private async Task EnsureInboundChannelAsync(
        string name,
        string contractFile,
        string responseMode,
        CancellationToken cancellationToken)
    {
        var document = Contracts.TryRead(contractFile)
            ?? throw new InvalidOperationException($"Контракт '{contractFile}' не найден.");
        var channels = await SendAsync(HttpMethod.Get, $"{Base}/inbound-channels", null, cancellationToken);

        if (Contains(channels?["channels"], name))
        {
            var uploaded = await SendAsync(
                HttpMethod.Post,
                $"{Base}/inbound-channels/{name}/contracts",
                new JsonObject
                {
                    ["channelName"] = name,
                    ["document"] = document,
                },
                cancellationToken);

            AssertSucceeded(uploaded, $"Загрузка контракта {name}");
        }
        else
        {
            var created = await SendAsync(
                HttpMethod.Post,
                $"{Base}/inbound-channels",
                new JsonObject
                {
                    ["name"] = name,
                    ["document"] = document,
                    ["responseMode"] = responseMode,
                },
                cancellationToken);

            AssertSucceeded(created, $"Создание inbound-канала {name}");
        }

        var channel = await SendAsync(HttpMethod.Get, $"{Base}/inbound-channels/{name}", null, cancellationToken);
        var draft = Draft(channel?["channel"]?["contractVersions"], "version")
            ?? throw new InvalidOperationException($"У канала {name} нет черновика контракта.");

        var settings = await SendAsync(
            HttpMethod.Patch,
            $"{Base}/inbound-channels/{name}/contracts/{draft}/settings",
            new JsonObject
            {
                ["settings"] = new JsonObject
                {
                    ["recipientQueue"] = $"{options.ProductId}.inbox",
                    ["authType"] = "none",
                },
            },
            cancellationToken);

        AssertSucceeded(settings, $"Настройки версии {name}");

        var published = await SendAsync(
            HttpMethod.Post,
            $"{Base}/inbound-channels/{name}/contracts/{draft}/publish",
            null,
            cancellationToken);

        AssertSucceeded(published, $"Публикация контракта {name}");

        var version = published?["version"]?.GetValue<int>()
            ?? throw new InvalidOperationException($"Ответ публикации контракта {name} не содержит версии.");

        var activated = await SendAsync(
            HttpMethod.Post,
            $"{Base}/inbound-channels/{name}/contracts/{version}/activate",
            null,
            cancellationToken);

        AssertSucceeded(activated, $"Активация контракта {name}");
    }

    private async Task EnsureProductRouteAsync(CancellationToken cancellationToken)
    {
        var existing = await TrySendAsync(
            HttpMethod.Get,
            $"{Base}/products/{options.ProductId}/route",
            null,
            cancellationToken);

        // Повторная регистрация уже настроенного маршрута в шлюзе не идемпотентна: пропускаем её.
        if (existing is not null && ContainsValue(existing["allowedChannels"], ProductSimulationOptions.OutboundChannel))
        {
            return;
        }

        var route = await SendAsync(
            HttpMethod.Put,
            $"{Base}/products/{options.ProductId}/route",
            new JsonObject
            {
                ["productId"] = options.ProductId,
                ["allowedChannels"] = new JsonArray(ProductSimulationOptions.OutboundChannel),
            },
            cancellationToken);

        AssertSucceeded(route, "Регистрация маршрута продукта");
    }

    /// <summary>Ждёт, пока живые экземпляры интеграции применят действующую конфигурацию.</summary>
    private async Task WaitUntilAppliedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var health = await SendAsync(
                HttpMethod.Get,
                $"{Base}/outbound-channels/{ProductSimulationOptions.OutboundChannel}/health",
                null,
                cancellationToken);

            var fresh = (health?["flowInstances"] as JsonArray)?
                .Where(node => node?["stale"]?.GetValue<bool>() == false)
                .ToArray() ?? [];

            if (fresh.Length > 0 && fresh.All(node => node?["upToDate"]?.GetValue<bool>() == true))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException("Конфигурация не применена экземплярами интеграции за 30 секунд.");
    }

    private JsonObject OutboundSettings() => new()
    {
        ["baseUrl"] = options.IntegrationBaseUrl,
        ["authType"] = "none",
        ["perAttemptTimeoutMs"] = 5000,
        ["overallCapMs"] = 15000,
        ["retryPolicy"] = new JsonArray(new JsonObject
        {
            ["match"] = "http_status_in",
            ["statusCodes"] = new JsonArray(503),
            ["delaysMs"] = new JsonArray(1000),
        }),
    };

    /// <summary>Проверяет, что активная ревизия канала соответствует настройкам симуляции.</summary>
    private bool IsCurrentOutboundSettings(JsonNode? activeSettings) =>
        activeSettings is JsonObject settings &&
        settings["baseUrl"]?.GetValue<string>() == options.IntegrationBaseUrl &&
        settings["authType"]?.GetValue<string>() == "none";

    private async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken) =>
        await SendCoreAsync(method, path, body, cancellationToken)
        ?? throw new InvalidOperationException($"Пустой ответ на {method} {path}.");

    private async Task<JsonNode?> TrySendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} -> {(int)response.StatusCode}: {text}");
        }

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<JsonNode?> SendCoreAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} -> {(int)response.StatusCode}: {text}");
        }

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, JsonNode? body)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static void AssertSucceeded(JsonNode? response, string operation)
    {
        if (response?["succeeded"]?.GetValue<bool>() == true)
        {
            return;
        }

        var problems = (response?["problems"] as JsonArray)?
            .Select(problem => $"{problem?["code"]}: {problem?["message"]}")
            .ToArray() ?? [];

        throw new InvalidOperationException(
            problems.Length == 0 ? $"Операция '{operation}' отклонена." : $"{operation} отклонена: {string.Join("; ", problems)}");
    }

    private static bool Contains(JsonNode? array, string name) =>
        array is JsonArray items &&
        items.Any(item => item?["name"]?.GetValue<string>() == name);

    /// <summary>Проверяет, что массив простых значений содержит строку.</summary>
    private static bool ContainsValue(JsonNode? array, string value) =>
        array is JsonArray items &&
        items.Any(item => item?.GetValue<string>() == value);

    /// <summary>Номер черновика в перечне ревизий или версий канала.</summary>
    private static int? Draft(JsonNode? items, string valueProperty)
    {
        if (items is not JsonArray array)
        {
            return null;
        }

        foreach (var item in array)
        {
            if (item?["status"]?.GetValue<string>() == "Draft")
            {
                return item[valueProperty]?.GetValue<int>();
            }
        }

        return null;
    }
}
