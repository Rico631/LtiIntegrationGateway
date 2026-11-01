namespace LtiIntegrationGateway.Simulation.Product;

/// <summary>OpenAPI-контракты продукта, встроенные в сборку.</summary>
public static class Contracts
{
    /// <summary>Контракт асинхронных входящих вызовов: <c>POST /order</c>.</summary>
    public const string AsyncOrders = "product-orders-async.json";

    /// <summary>Контракт синхронных входящих вызовов: <c>GET /order/{orderid}</c>.</summary>
    public const string SyncOrders = "product-orders-sync.json";

    /// <summary>Возвращает документ по имени или <c>null</c>, если такого контракта нет.</summary>
    public static string? TryRead(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Имя приходит и из маршрута (/contracts/product-orders-async), и из Swagger (/contracts/product-orders-async.json).
        var fileName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.json";

        if (fileName is not (AsyncOrders or SyncOrders))
        {
            return null;
        }

        using var stream = typeof(Contracts).Assembly.GetManifestResourceStream(fileName)
            ?? throw new InvalidOperationException($"Контракт '{fileName}' не встроен в сборку.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
