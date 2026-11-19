namespace FlyAotOrm;

public sealed class QueryJoin<TEntity, TJoin>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;
    private readonly DbRowMaterializer<TEntity> _materializer;
    private readonly TableNode _joinTable;
    private readonly JoinKind _kind;

    internal QueryJoin(FlyDb db, SelectNode node, DbRowMaterializer<TEntity> materializer, TableNode joinTable, JoinKind kind)
    {
        _db = db;
        _node = node;
        _materializer = materializer;
        _joinTable = joinTable;
        _kind = kind;
    }

    public Query<TEntity> On(SqlCondition condition)
    {
        var joins = _node.Joins.Concat(new[] { new JoinNode(_joinTable, condition, _kind) }).ToArray();
        return new Query<TEntity>(_db, _node with { Joins = joins }, _materializer);
    }
}
