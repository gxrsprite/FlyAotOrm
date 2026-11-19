namespace FlyAotOrm;

public sealed class ProjectionFromBuilder<TProjection>
{
    private readonly FlyDb _db;

    internal ProjectionFromBuilder(FlyDb db) => _db = db;

    public ProjectionQuery<TProjection> From<TEntity>(DbTable<TEntity> table)
        => new(_db, new SelectNode(table.ToNode(), null, Array.Empty<JoinNode>(), Array.Empty<SelectedColumn>(), Array.Empty<SqlOrder>(), null, null, InlineParameters: false));
}
