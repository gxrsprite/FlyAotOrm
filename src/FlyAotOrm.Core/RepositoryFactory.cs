using System.Data.Common;

namespace FlyAotOrm;

public delegate TRepository RepositoryActivator<out TRepository>(DbConnection connection, DbTransaction? transaction);

public sealed class RepositoryFactory<TRepository>
{
    private readonly RepositoryActivator<TRepository> _activator;

    public RepositoryFactory(RepositoryActivator<TRepository> activator)
    {
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
    }

    public TRepository Create(DbConnection connection, DbTransaction? transaction = null)
        => _activator(connection, transaction);
}
