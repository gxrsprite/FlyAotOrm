namespace FlyAotOrm;

public sealed class CountQuery<TEntity>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;

    internal CountQuery(FlyDb db, SelectNode node)
    {
        _db = db;
        _node = node;
    }

    public CountQuery<TEntity> Where(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public CountQuery<TEntity> WhereIf(bool condition, SqlCondition sqlCondition)
        => condition ? Where(sqlCondition) : this;

    public CountQuery<TEntity> And(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public CountQuery<TEntity> AndIf(bool condition, SqlCondition sqlCondition)
        => condition ? And(sqlCondition) : this;

    public CountQuery<TEntity> Or(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.Or(condition) });

    public CountQuery<TEntity> OrIf(bool condition, SqlCondition sqlCondition)
        => condition ? Or(sqlCondition) : this;

    public CountQuery<TEntity> InlineParameters()
        => With(_node with { InlineParameters = true });

    public CountQuery<TEntity> NoneParameter()
        => InlineParameters();

    public CountQuery<TEntity> NoneParamter()
        => InlineParameters();

    public CountQueryJoin<TEntity, TJoin> Join<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Inner);

    public CountQueryJoin<TEntity, TJoin> LeftJoin<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Left);

    public RenderedSql ToSql()
        => _db.Renderer.RenderCount(_node);

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToSql();
        await using var command = QueryCommands.Create(_db, rendered);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private CountQuery<TEntity> With(SelectNode node)
        => new(_db, node);
}
