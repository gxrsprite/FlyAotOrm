using System.Data.Common;

namespace FlyAotOrm;

public sealed class FlyDb
{
    public FlyDb(DbConnection connection, DbDialectKind dialect, DbTransaction? transaction = null)
        : this(connection, SqlRenderers.Create(dialect), transaction)
    {
    }

    public FlyDb(DbConnection connection, DbDialectKind dialect, SqlRendererOptions rendererOptions, DbTransaction? transaction = null)
        : this(connection, SqlRenderers.Create(dialect, rendererOptions), transaction)
    {
    }

    public FlyDb(DbConnection connection, ISqlRenderer renderer, DbTransaction? transaction = null)
    {
        Connection = connection;
        Renderer = renderer;
        Transaction = transaction;
    }

    public DbConnection Connection { get; }

    public DbTransaction? Transaction { get; }

    public ISqlRenderer Renderer { get; }

    public Query<TEntity> Select<TEntity>(DbTable<TEntity> table)
        => new(this, new SelectNode(table.ToNode(), null, Array.Empty<JoinNode>(), Array.Empty<SelectedColumn>(), Array.Empty<SqlOrder>(), null, null, InlineParameters: false), table.Materializer);

    public CountQuery<TEntity> SelectCount<TEntity>(DbTable<TEntity> table)
        => new(this, new SelectNode(table.ToNode(), null, Array.Empty<JoinNode>(), Array.Empty<SelectedColumn>(), Array.Empty<SqlOrder>(), null, null, InlineParameters: false));

    public AnyQuery<TEntity> SelectAny<TEntity>(DbTable<TEntity> table)
        => new(this, new SelectNode(table.ToNode(), null, Array.Empty<JoinNode>(), Array.Empty<SelectedColumn>(), Array.Empty<SqlOrder>(), null, null, InlineParameters: false));

    public ProjectionFromBuilder<TProjection> Select<TProjection>()
        => new(this);
}
