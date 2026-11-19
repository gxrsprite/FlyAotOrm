namespace FlyAotOrm;

public static class FlyTenant
{
    private static readonly AsyncLocal<object?> CurrentTenant = new();

    public static IDisposable Use<TTenant>(TTenant tenantId)
    {
        var previous = CurrentTenant.Value;
        CurrentTenant.Value = tenantId;
        return new Scope(previous);
    }

    public static void Set<TTenant>(TTenant tenantId)
        => CurrentTenant.Value = tenantId;

    public static void Clear()
        => CurrentTenant.Value = null;

    public static TTenant Get<TTenant>()
    {
        var value = CurrentTenant.Value;
        if (value is null)
        {
            throw new InvalidOperationException("No FlyAotOrm tenant is active. Use FlyTenant.Use(...) before querying an entity with [DbTenant].");
        }

        return value is TTenant typed
            ? typed
            : FlyDbValueConverter.Read<TTenant>(value);
    }

    private sealed class Scope : IDisposable
    {
        private readonly object? _previous;
        private bool _disposed;

        public Scope(object? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentTenant.Value = _previous;
            _disposed = true;
        }
    }
}
