namespace FlyAotOrm;

public sealed class CountQueryJoin<TEntity, TJoin>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;
    private readonly TableNode _joinTable;
    private readonly JoinKind _kind;

    internal CountQueryJoin(FlyDb db, SelectNode node, TableNode joinTable, JoinKind kind)
    {
        _db = db;
        _node = node;
        _joinTable = joinTable;
        _kind = kind;
    }

    public CountQuery<TEntity> On(SqlCondition condition)
    {
        var joins = _node.Joins.Concat(new[] { new JoinNode(_joinTable, condition, _kind) }).ToArray();
        return new CountQuery<TEntity>(_db, _node with { Joins = joins });
    }
}
