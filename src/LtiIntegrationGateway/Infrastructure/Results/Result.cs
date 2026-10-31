namespace LtiIntegrationGateway.Infrastructure.Results;

/// <summary>Фабрика результатов операций.</summary>
public static class Result
{
    /// <summary>Создаёт успешный результат.</summary>
    public static Result<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(value, null, null);
    }

    /// <summary>Создаёт отказ с устойчивым кодом и безопасным описанием.</summary>
    public static Result<T> Failure<T>(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new Result<T>(default, errorCode, errorMessage);
    }
}

/// <summary>
/// Результат операции: успешное значение либо безопасные код и описание ошибки.
/// </summary>
public sealed record Result<T>
{
    internal Result(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Значение успешной операции; <c>null</c> при отказе.</summary>
    public T? Value { get; }

    /// <summary>Устойчивый код отказа; <c>null</c> при успехе.</summary>
    public string? ErrorCode { get; }

    /// <summary>Безопасное описание отказа; <c>null</c> при успехе.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Признак успешного результата.</summary>
    public bool IsSuccess => ErrorCode is null;
}
