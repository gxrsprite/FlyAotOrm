namespace FlyAotOrm;

public sealed record SqlParameter(string Name, object? Value);

public sealed record RenderedSql(string Sql, IReadOnlyList<SqlParameter> Parameters)
{
    public bool TryGetParameter(string name, out SqlParameter parameter)
    {
        var normalizedName = NormalizeParameterName(name);
        foreach (var candidate in Parameters)
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, normalizedName))
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = default!;
        return false;
    }

    public object? GetParameterValue(string name)
    {
        if (TryGetParameter(name, out var parameter))
        {
            return parameter.Value;
        }

        throw new KeyNotFoundException("SQL parameter was not found: " + name);
    }

    public IReadOnlyDictionary<string, object?> ToParameterDictionary()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            values.Add(parameter.Name, parameter.Value);
        }

        return values;
    }

    private static string NormalizeParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Parameter name must not be empty.", nameof(name));
        }

        return name[0] is '@' or ':' or '$' or '?'
            ? name.Substring(1)
            : name;
    }
}

public abstract record SqlExpression;

public sealed record ColumnExpression(string TableAlias, string ColumnName) : SqlExpression;

public sealed record ValueExpression(object? Value) : SqlExpression;

public abstract record SqlCondition;

public sealed record BinaryCondition(SqlExpression Left, string Operator, SqlExpression Right) : SqlCondition;

public sealed record LikeCondition(SqlExpression Left, string Pattern) : SqlCondition;

public sealed record InCondition(SqlExpression Left, IReadOnlyList<object?> Values) : SqlCondition;

public sealed record BetweenCondition(SqlExpression Left, object? Lower, object? Upper) : SqlCondition;

public sealed record NullCondition(SqlExpression Left, bool IsNot) : SqlCondition;

public sealed record CombinedCondition(SqlCondition Left, string Operator, SqlCondition Right) : SqlCondition;

public sealed record SqlOrder(ColumnExpression Column, bool Descending);

public sealed record SelectedColumn(ColumnExpression Column, string Alias);

public sealed record TableNode(Type EntityType, string TableName, string Alias);

public enum JoinKind
{
    Inner,
    Left
}

public sealed record JoinNode(TableNode Table, SqlCondition On, JoinKind Kind);

public sealed record SelectNode(
    TableNode Table,
    SqlCondition? Where,
    IReadOnlyList<JoinNode> Joins,
    IReadOnlyList<SelectedColumn> Columns,
    IReadOnlyList<SqlOrder> Orders,
    int? Limit,
    int? Offset,
    bool InlineParameters);
