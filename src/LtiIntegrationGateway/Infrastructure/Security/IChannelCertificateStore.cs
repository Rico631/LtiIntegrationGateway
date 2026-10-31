using System.Security.Cryptography.X509Certificates;

namespace LtiIntegrationGateway.Infrastructure.Security;

/// <summary>
/// Результат загрузки материала клиентского сертификата: готовый сертификат или причина отказа.
/// </summary>
/// <remarks>
/// Причина отказа описывает только характер проблемы и никогда не содержит значение материала,
/// пароль или отпечаток: результат попадает в <c>OutboundResult</c> и журнал.
/// </remarks>
public sealed record ChannelCertificateResult
{
    private ChannelCertificateResult(X509Certificate2? certificate, string? error)
    {
        Certificate = certificate;
        Error = error;
    }

    /// <summary>Загруженный сертификат либо <c>null</c>, если материал непригоден.</summary>
    public X509Certificate2? Certificate { get; }

    /// <summary>Причина отказа без значения материала; <c>null</c> при успешной загрузке.</summary>
    public string? Error { get; }

    /// <summary>Возвращает успешный результат с загруженным сертификатом.</summary>
    public static ChannelCertificateResult Loaded(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new ChannelCertificateResult(certificate, null);
    }

    /// <summary>Возвращает отказ с причиной, не содержащей значения материала.</summary>
    public static ChannelCertificateResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new ChannelCertificateResult(null, error);
    }
}

/// <summary>
/// Диагностические сведения о клиентском сертификате канала: найден ли материал, когда истекает срок
/// и пригоден ли сертификат к использованию.
/// </summary>
/// <remarks>
/// В отличие от <see cref="IChannelCertificateStore.Load"/> метод не требует валидности: срок и причина
/// отказа доступны и для истёкшего или ещё не вступившего в силу сертификата. Причина описывает только
/// характер проблемы и никогда не содержит значение материала или пароль.
/// </remarks>
public sealed record ChannelCertificateInfo
{
    private ChannelCertificateInfo(
        bool found,
        DateTimeOffset? expiresAt,
        bool valid,
        bool notYetValid,
        string? problem)
    {
        Found = found;
        ExpiresAt = expiresAt;
        Valid = valid;
        NotYetValid = notYetValid;
        Problem = problem;
    }

    /// <summary>Признак того, что материал найден и разбирается как PFX.</summary>
    public bool Found { get; }

    /// <summary>Срок действия сертификата в UTC; <c>null</c>, если материал не найден или не читается.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Признак пригодности к использованию: материал разобран, есть приватный ключ, срок идёт.</summary>
    public bool Valid { get; }

    /// <summary>Признак того, что срок действия сертификата ещё не наступил.</summary>
    public bool NotYetValid { get; }

    /// <summary>Причина непригодности без значения материала и пароля; <c>null</c> для валидного сертификата.</summary>
    public string? Problem { get; }

    /// <summary>Материал не найден или не разобран: срока и валидности нет.</summary>
    public static ChannelCertificateInfo Missing(string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);

        return new ChannelCertificateInfo(false, null, false, false, problem);
    }

    /// <summary>Материал разобран: срок известен, валидность и причина рассчитаны.</summary>
    public static ChannelCertificateInfo Parsed(
        DateTimeOffset expiresAt,
        bool valid,
        bool notYetValid,
        string? problem) =>
        new(true, expiresAt, valid, notYetValid, problem);
}

/// <summary>Источник клиентских сертификатов исходящих каналов по ключу конфигурации ревизии.</summary>
/// <remarks>
/// Хранилище владеет загруженными сертификатами: экземпляр живёт столько же, сколько приложение,
/// и освобождается при его остановке. Вызывающий сертификат не освобождает.
/// </remarks>
public interface IChannelCertificateStore
{
    /// <summary>
    /// Загружает материал сертификата по ключу конфигурации активной ревизии. Любая проблема
    /// (ключ не задан, значение отсутствует, PFX не читается, нет приватного ключа, истёк срок)
    /// возвращает отказ без значения материала и без пароля.
    /// </summary>
    /// <remarks>
    /// Повторные вызовы для одного ключа возвращают один и тот же экземпляр сертификата: PFX
    /// разбирается один раз на время жизни приложения. Возвращённый сертификат принадлежит
    /// хранилищу, вызывающий его не освобождает.
    /// </remarks>
    ChannelCertificateResult Load(string? configKey);

    /// <summary>
    /// Возвращает диагностические сведения о материале сертификата без требования валидности: срок
    /// действия доступен и для истёкшего или ещё не вступившего в силу сертификата. Значение материала
    /// и пароль не раскрываются, причина отказа описывает только характер проблемы.
    /// </summary>
    /// <remarks>
    /// Сертификат, загруженный специально для диагностики, освобождается внутри метода; закэшированный
    /// экземпляр остаётся собственностью хранилища.
    /// </remarks>
    ChannelCertificateInfo GetInfo(string? configKey);
}
