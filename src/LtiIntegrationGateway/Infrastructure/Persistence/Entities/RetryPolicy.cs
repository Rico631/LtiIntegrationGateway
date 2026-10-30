namespace LtiIntegrationGateway.Infrastructure.Persistence.Entities;

/// <summary>Признак совпадения правила повторов.</summary>
public enum RetryMatchType
{
    /// <summary>Транспортная ошибка: таймаут, отказ соединения, TLS.</summary>
    TransportError = 1,

    /// <summary>Ответ с одним из перечисленных HTTP-статусов.</summary>
    HttpStatusIn = 2,
}

/// <summary>Правило политики повторов канала.</summary>
/// <param name="Match">Признак, по которому правило применяется.</param>
/// <param name="StatusCodes">HTTP-статусы для <see cref="RetryMatchType.HttpStatusIn"/>.</param>
/// <param name="DelaysMs">Задержки перед повторами; число повторов равно длине массива.</param>
public sealed record RetryRule(RetryMatchType Match, IReadOnlyList<int> StatusCodes, IReadOnlyList<int> DelaysMs)
{
    public static RetryRule TransportError(IReadOnlyList<int> delaysMs) =>
        new(RetryMatchType.TransportError, [], delaysMs);

    public static RetryRule HttpStatusIn(IReadOnlyList<int> statusCodes, IReadOnlyList<int> delaysMs) =>
        new(RetryMatchType.HttpStatusIn, statusCodes, delaysMs);
}

/// <summary>Политика повторов канала: упорядоченный список правил.</summary>
/// <remarks>Хранится JSON-документом внутри ревизии: правила упорядочены и меняются только вместе с ней.</remarks>
public sealed record RetryPolicy(IReadOnlyList<RetryRule> Rules)
{
    public static RetryPolicy Empty { get; } = new([]);
}
