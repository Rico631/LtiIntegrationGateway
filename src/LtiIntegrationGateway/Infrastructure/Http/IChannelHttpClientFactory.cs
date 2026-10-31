using System.Security.Cryptography.X509Certificates;

namespace LtiIntegrationGateway.Infrastructure.Http;

/// <summary>
/// Источник HTTP-обработчиков исходящих каналов. Абстракция отделяет выполнение вызова от пула,
/// поэтому в тестах вместо реальных соединений подставляется управляемый обработчик.
/// </summary>
public interface IChannelHttpClientFactory
{
    /// <summary>
    /// Возвращает аренду обработчика для ключа канала. Клиентский сертификат используется только
    /// при создании нового обработчика; при переиспользовании записи пула остаётся её сертификат.
    /// </summary>
    /// <remarks>
    /// Сертификат принадлежит <c>IChannelCertificateStore</c> и живёт столько же, сколько
    /// приложение: обработчик пула его не освобождает, и вызывающий не должен этого делать.
    /// </remarks>
    ChannelHttpClientLease Acquire(ChannelHttpClientKey key, X509Certificate2? clientCertificate);
}
