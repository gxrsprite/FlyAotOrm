namespace FlyAotOrm;

public sealed class AnyQuery<TEntity>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;

    internal AnyQuery(FlyDb db, SelectNode node)
    {
        _db = db;
        _node = node;
    }

    public AnyQuery<TEntity> Where(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public AnyQuery<TEntity> WhereIf(bool condition, SqlCondition sqlCondition)
        => condition ? Where(sqlCondition) : this;

    public AnyQuery<TEntity> And(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public AnyQuery<TEntity> AndIf(bool condition, SqlCondition sqlCondition)
        => condition ? And(sqlCondition) : this;

    public AnyQuery<TEntity> Or(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.Or(condition) });

    public AnyQuery<TEntity> OrIf(bool condition, SqlCondition sqlCondition)
        => condition ? Or(sqlCondition) : this;

    public AnyQuery<TEntity> InlineParameters()
        => With(_node with { InlineParameters = true });

    public AnyQuery<TEntity> NoneParameter()
        => InlineParameters();

    public AnyQuery<TEntity> NoneParamter()
        => InlineParameters();

    public AnyQueryJoin<TEntity, TJoin> Join<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Inner);

    public AnyQueryJoin<TEntity, TJoin> LeftJoin<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Left);

    public RenderedSql ToSql()
        => _db.Renderer.RenderAny(_node);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToSql();
        await using var command = QueryCommands.Create(_db, rendered);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private AnyQuery<TEntity> With(SelectNode node)
        => new(_db, node);
}
