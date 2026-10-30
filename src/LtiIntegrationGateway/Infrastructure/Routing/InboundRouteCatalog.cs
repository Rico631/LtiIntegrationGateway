namespace LtiIntegrationGateway.Infrastructure.Routing;

/// <summary>
/// Каталог активных наборов входящих маршрутов. Хранит наборы по ключу ревизии и отдаёт наружу
/// неизменяемый снапшот активных маршрутов; при изменении оповещает подписчиков токеном.
/// </summary>
public sealed class InboundRouteCatalog
{
    private readonly InboundRouteSetValidator _validator = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, InboundRouteSet> _sets = new(StringComparer.Ordinal);

    private InboundRouteDefinition[] _snapshot = [];
    private CancellationTokenSource _changeToken = new();

    /// <summary>Снапшот активных маршрутов, упорядоченный по ключу ревизии.</summary>
    public IReadOnlyList<InboundRouteDefinition> Snapshot() => Volatile.Read(ref _snapshot);

    /// <summary>Токен, отменяемый при каждом изменении состава активных маршрутов.</summary>
    public CancellationToken ChangeToken => Volatile.Read(ref _changeToken).Token;

    /// <summary>Ключи активных наборов; нужны для диагностики и проверок.</summary>
    public IReadOnlyList<string> ActiveRevisionKeys
    {
        get
        {
            lock (_gate)
            {
                return _sets.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>
    /// Активирует набор. При проблемах валидации состояние не меняется и возвращаются проблемы;
    /// повторная активация с тем же ключом ревизии заменяет набор.
    /// </summary>
    public InboundRouteActivationResult Activate(InboundRouteSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        lock (_gate)
        {
            var problems = _validator.Validate(set, _sets.Values.ToArray());

            if (problems.Count > 0)
            {
                return new InboundRouteActivationResult(problems);
            }

            _sets[set.RevisionKey.Trim()] = set;

            RebuildSnapshot();
            NotifyChanged();

            return new InboundRouteActivationResult([]);
        }
    }

    /// <summary>Деактивирует набор по ключу ревизии. Возвращает <c>false</c>, если набора не было.</summary>
    public bool Deactivate(string revisionKey)
    {
        if (string.IsNullOrWhiteSpace(revisionKey))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_sets.Remove(revisionKey.Trim()))
            {
                return false;
            }

            RebuildSnapshot();
            NotifyChanged();

            return true;
        }
    }

    private void RebuildSnapshot() =>
        Volatile.Write(
            ref _snapshot,
            _sets
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value.Routes)
                .ToArray());

    private void NotifyChanged()
    {
        var previous = Interlocked.Exchange(ref _changeToken, new CancellationTokenSource());

        previous.Cancel();
    }
}
