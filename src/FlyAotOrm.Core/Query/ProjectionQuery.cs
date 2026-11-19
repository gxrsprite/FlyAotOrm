using System.Data.Common;

namespace FlyAotOrm;

public sealed class ProjectionQuery<TProjection>
{
    private readonly FlyDb _db;
    private readonly SelectNode _node;

    internal ProjectionQuery(FlyDb db, SelectNode node)
    {
        _db = db;
        _node = node;
    }

    public ProjectionQuery<TProjection> Columns(params SelectedColumn[] columns)
        => With(_node with { Columns = columns });

    public ProjectionQuery<TProjection> Where(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public ProjectionQuery<TProjection> WhereIf(bool condition, SqlCondition sqlCondition)
        => condition ? Where(sqlCondition) : this;

    public ProjectionQuery<TProjection> And(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.And(condition) });

    public ProjectionQuery<TProjection> AndIf(bool condition, SqlCondition sqlCondition)
        => condition ? And(sqlCondition) : this;

    public ProjectionQuery<TProjection> Or(SqlCondition condition)
        => With(_node with { Where = _node.Where is null ? condition : _node.Where.Or(condition) });

    public ProjectionQuery<TProjection> OrIf(bool condition, SqlCondition sqlCondition)
        => condition ? Or(sqlCondition) : this;

    public ProjectionQuery<TProjection> OrderBy(params SqlOrder[] orders)
        => With(_node with { Orders = orders });

    public ProjectionQuery<TProjection> Take(int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Take count must be greater than or equal to 1.");
        }

        return With(_node with { Limit = count });
    }

    public ProjectionQuery<TProjection> Skip(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Skip count must be greater than or equal to 0.");
        }

        return With(_node with { Offset = count });
    }

    public ProjectionQuery<TProjection> Page(int page, int pageSize = 20)
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

    public ProjectionQuery<TProjection> InlineParameters()
        => With(_node with { InlineParameters = true });

    public ProjectionQuery<TProjection> NoneParameter()
        => InlineParameters();

    public ProjectionQuery<TProjection> NoneParamter()
        => InlineParameters();

    public ProjectionJoin<TProjection, TJoin> Join<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Inner);

    public ProjectionJoin<TProjection, TJoin> LeftJoin<TJoin>(DbTable<TJoin> table)
        => new(_db, _node, table.ToNode(), JoinKind.Left);

    public RenderedSql ToSql() => _db.Renderer.Render(_node);

    public RenderedSql ToFirstSql() => _db.Renderer.Render(_node with { Limit = 1 });

    public RenderedSql ToCountSql() => _db.Renderer.RenderCount(_node);

    public RenderedSql ToAnySql() => _db.Renderer.RenderAny(_node);

    public async Task<IReadOnlyList<TProjection>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToSql();
        await using var command = QueryCommands.Create(_db, rendered);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<TProjection>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(Materialize(reader));
        }

        return rows;
    }

    public async Task<TProjection?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var rendered = ToFirstSql();
        await using var command = QueryCommands.Create(_db, rendered);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Materialize(reader)
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

    private ProjectionQuery<TProjection> With(SelectNode node) => new(_db, node);

    private static TProjection Materialize(DbDataReader reader)
    {
        if (FlyProjectionMaterializerRegistry.TryGet<TProjection>(out var generated))
        {
            return generated(reader);
        }

        throw new InvalidOperationException("No generated projection materializer is registered for " + typeof(TProjection).FullName + ".");
    }
}
