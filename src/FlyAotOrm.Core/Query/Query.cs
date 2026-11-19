namespace FlyAotOrm;

public sealed class Query<TEntity>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;
    private readonly DbRowMaterializer<TEntity> _materializer;

    internal Query(FlyDb db, SelectNode node, DbRowMaterializer<TEntity> materializer)
    {
        _db = db;
        _node = node;
        _materializer = materializer;
    }

    public Query<TEntity> Where(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public Query<TEntity> WhereIf(bool condition, SqlCondition sqlCondition)
        => condition ? Where(sqlCondition) : this;

    public Query<TEntity> And(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public Query<TEntity> AndIf(bool condition, SqlCondition sqlCondition)
        => condition ? And(sqlCondition) : this;

    public Query<TEntity> Or(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.Or(condition) });

    public Query<TEntity> OrIf(bool condition, SqlCondition sqlCondition)
        => condition ? Or(sqlCondition) : this;

    public Query<TEntity> OrderBy(params SqlOrder[] orders)
        => With(_node with { Orders = orders });

    public Query<TEntity> Take(int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Take count must be greater than or equal to 1.");
        }

        return With(_node with { Limit = count });
    }

    public Query<TEntity> Skip(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Skip count must be greater than or equal to 0.");
        }

        return With(_node with { Offset = count });
    }

    public Query<TEntity> Page(int page, int pageSize = 20)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be greater than or equal to 1.");
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than or equal to 1.");
        }

        return With(_node with { Limit = pageSize, Offset = (page - 1) * pageSize });
    }

    public Query<TEntity> InlineParameters()
        => With(_node with { InlineParameters = true });

    public Query<TEntity> NoneParameter()
        => InlineParameters();

    public Query<TEntity> NoneParamter()
        => InlineParameters();

    public QueryJoin<TEntity, TJoin> Join<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, _materializer, table.ToNode(), JoinKind.Inner);

    public QueryJoin<TEntity, TJoin> LeftJoin<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, _materializer, table.ToNode(), JoinKind.Left);

    public RenderedSql ToSql() => _db.Renderer.Render(_node);

    public RenderedSql ToFirstSql() => _db.Renderer.Render(_node with { Limit = 1 });

    public RenderedSql ToCountSql() => _db.Renderer.RenderCount(_node);

    public RenderedSql ToAnySql() => _db.Renderer.RenderAny(_node);

    public async Task<IReadOnlyList<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToSql();
        await using var command = QueryCommands.Create(_db, rendered);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<TEntity>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(_materializer(reader));
        }

        return rows;
    }

    public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToFirstSql();
        await using var command = QueryCommands.Create(_db, rendered);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? _materializer(reader)
            : default;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToCountSql();
        await using var command = QueryCommands.Create(_db, rendered);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToAnySql();
        await using var command = QueryCommands.Create(_db, rendered);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private Query<TEntity> With(SelectNode node) => new(_db, node, _materializer);
}
