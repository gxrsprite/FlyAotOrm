using System.Text;

namespace FlyAotOrm;

public abstract class SqlRenderer : ISqlRenderer
{
    private readonly SqlRendererOptions _options;

    protected SqlRenderer(SqlRendererOptions? options = null)
    {
        _options = options ?? SqlRendererOptions.Default;
    }

    public RenderedSql Render(SelectNode node)
    {
        var parameters = new List<SqlParameter>();
        var builder = new StringBuilder();

        builder.Append("select ");
        builder.Append(RenderSelectColumns(node));
        AppendFromWhere(builder, node, parameters);

        if (node.Orders.Count > 0)
        {
            builder.Append(" order by ");
            builder.Append(string.Join(", ", node.Orders.Select(RenderOrder)));
        }

        AppendPaging(builder, node, parameters);

        return new RenderedSql(builder.ToString(), parameters);
    }

    public RenderedSql RenderCount(SelectNode node)
    {
        var parameters = new List<SqlParameter>();
        var builder = new StringBuilder();

        builder.Append("select count(1) from ");
        AppendTableAndWhere(builder, node, parameters);

        return new RenderedSql(builder.ToString(), parameters);
    }

    public RenderedSql RenderAny(SelectNode node)
    {
        var parameters = new List<SqlParameter>();
        var builder = new StringBuilder();

        builder.Append("select case when ");
        builder.Append(RenderExistenceCheck(node, parameters));
        builder.Append(" then 1 else 0 end");

        return new RenderedSql(builder.ToString(), parameters);
    }

    /// <summary>
    /// Renders the existence check used by <see cref="RenderAny"/>.
    /// NovaDb has no <c>EXISTS (subquery)</c> keyword, so its renderer overrides this.
    /// </summary>
    protected virtual string RenderExistenceCheck(SelectNode node, List<SqlParameter> parameters)
    {
        var builder = new StringBuilder();
        builder.Append("exists (select 1 from ");
        AppendTableAndWhere(builder, node, parameters);
        builder.Append(')');
        return builder.ToString();
    }

    protected abstract string Quote(string identifier);

    protected virtual string ParameterPrefix => "@";

    protected virtual void AppendPaging(StringBuilder builder, SelectNode node, List<SqlParameter> parameters)
    {
        if (node.Limit is null)
        {
            return;
        }

        builder.Append(" limit ");
        AppendPagingValue(builder, parameters, "limit", node.Limit.Value, node.InlineParameters);

        if (node.Offset is not null)
        {
            builder.Append(" offset ");
            AppendPagingValue(builder, parameters, "offset", node.Offset.Value, node.InlineParameters);
        }
    }

    private void AppendFromWhere(StringBuilder builder, SelectNode node, List<SqlParameter> parameters)
    {
        builder.Append(" from ");
        AppendTableAndWhere(builder, node, parameters);
    }

    private void AppendTableAndWhere(StringBuilder builder, SelectNode node, List<SqlParameter> parameters)
    {
        builder.Append(Quote(node.Table.TableName));
        builder.Append(" as ");
        builder.Append(Quote(node.Table.Alias));

        foreach (var join in node.Joins)
        {
            builder.Append(join.Kind == JoinKind.Left ? " left join " : " join ");
            builder.Append(Quote(join.Table.TableName));
            builder.Append(" as ");
            builder.Append(Quote(join.Table.Alias));
            builder.Append(" on ");
            builder.Append(RenderCondition(join.On, parameters, node.InlineParameters));
        }

        if (node.Where is not null)
        {
            builder.Append(" where ");
            builder.Append(RenderCondition(node.Where, parameters, node.InlineParameters));
        }
    }

    private string RenderSelectColumns(SelectNode node)
    {
        if (node.Columns.Count == 0)
        {
            return RenderSelectAllColumns(node);
        }

        return string.Join(", ", node.Columns.Select(RenderSelectedColumn));
    }

    /// <summary>
    /// Renders <c>SELECT *</c> for column-less selects.
    /// NovaDb rejects a qualified star (<c>`a`.*</c>), so its renderer returns a bare <c>*</c>.
    /// </summary>
    protected virtual string RenderSelectAllColumns(SelectNode node)
        => Quote(node.Table.Alias) + ".*";

    private string RenderSelectedColumn(SelectedColumn selectedColumn)
        => RenderColumn(selectedColumn.Column) + " as " + Quote(selectedColumn.Alias);

    private string RenderOrder(SqlOrder order)
        => RenderColumn(order.Column) + (order.Descending ? " desc" : " asc");

    private string RenderColumn(ColumnExpression column)
        => Quote(column.TableAlias) + "." + Quote(column.ColumnName);

    protected string RenderValue(object? value, List<SqlParameter> parameters)
    {
        var name = AddParameter(parameters, _options.ValueParameterNamePrefix, value);
        return ParameterPrefix + name;
    }

    protected string RenderValue(object? value, List<SqlParameter> parameters, bool inlineParameters)
    {
        if (inlineParameters && (TryRenderInlineLiteral(value, out var literal) || TryRenderLiteral(value, out literal)))
        {
            return literal;
        }

        return RenderValue(value, parameters);
    }

    /// <summary>
    /// Dialect-specific inline literals, consulted before <see cref="TryRenderLiteral"/>.
    /// NovaDb overrides this for booleans: its engine never coerces <c>bool = 1</c>,
    /// so inline booleans must be <c>TRUE</c>/<c>FALSE</c> keywords.
    /// </summary>
    protected virtual bool TryRenderInlineLiteral(object? value, out string literal)
    {
        literal = string.Empty;
        return false;
    }

    protected string RenderExpression(SqlExpression expression, List<SqlParameter> parameters, bool inlineParameters)
    {
        return expression switch
        {
            ColumnExpression column => RenderColumn(column),
            ValueExpression value => RenderValue(value.Value, parameters, inlineParameters),
            _ => throw new NotSupportedException("Unsupported SQL expression: " + expression.GetType().Name)
        };
    }

    protected string RenderCondition(SqlCondition condition, List<SqlParameter> parameters, bool inlineParameters)
    {
        return condition switch
        {
            BinaryCondition binary => RenderBinaryCondition(binary, parameters, inlineParameters),
            LikeCondition like => RenderExpression(like.Left, parameters, inlineParameters) + " like " + RenderValue(like.Pattern, parameters, inlineParameters),
            InCondition input => RenderIn(input, parameters, inlineParameters),
            BetweenCondition between => RenderBetweenCondition(between, parameters, inlineParameters),
            NullCondition isNull => RenderExpression(isNull.Left, parameters, inlineParameters) + (isNull.IsNot ? " is not null" : " is null"),
            CombinedCondition combined => "(" + RenderCondition(combined.Left, parameters, inlineParameters) + " " + combined.Operator + " " + RenderCondition(combined.Right, parameters, inlineParameters) + ")",
            _ => throw new NotSupportedException("Unsupported SQL condition: " + condition.GetType().Name)
        };
    }

    private string RenderBinaryCondition(BinaryCondition binary, List<SqlParameter> parameters, bool inlineParameters)
    {
        if (binary.Right is ValueExpression { Value: null } && binary.Left is ColumnExpression)
        {
            return binary.Operator switch
            {
                "=" => RenderExpression(binary.Left, parameters, inlineParameters) + " is null",
                "<>" => RenderExpression(binary.Left, parameters, inlineParameters) + " is not null",
                _ => RenderExpression(binary.Left, parameters, inlineParameters) + " " + binary.Operator + " " + RenderExpression(binary.Right, parameters, inlineParameters)
            };
        }

        return RenderExpression(binary.Left, parameters, inlineParameters) + " " + binary.Operator + " " + RenderExpression(binary.Right, parameters, inlineParameters);
    }

    /// <summary>
    /// Renders <c>BETWEEN</c>. NovaDb lexes but never parses it, so its renderer
    /// expands to <c>&gt;= … AND … &lt;=</c>.
    /// </summary>
    protected virtual string RenderBetweenCondition(BetweenCondition between, List<SqlParameter> parameters, bool inlineParameters)
        => RenderExpression(between.Left, parameters, inlineParameters) + " between " + RenderValue(between.Lower, parameters, inlineParameters) + " and " + RenderValue(between.Upper, parameters, inlineParameters);

    private string RenderIn(InCondition condition, List<SqlParameter> parameters, bool inlineParameters)
    {
        if (condition.Values.Count == 0)
        {
            return "1 = 0";
        }

        var names = condition.Values.Select(value => RenderValue(value, parameters, inlineParameters));
        return RenderExpression(condition.Left, parameters, inlineParameters) + " in (" + string.Join(", ", names) + ")";
    }

    protected void AppendPagingValue(StringBuilder builder, List<SqlParameter> parameters, string prefix, int value, bool inlineParameters)
    {
        if (inlineParameters)
        {
            builder.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        var parameterNamePrefix = prefix switch
        {
            "limit" => _options.LimitParameterNamePrefix,
            "offset" => _options.OffsetParameterNamePrefix,
            _ => prefix
        };
        var name = AddParameter(parameters, parameterNamePrefix, value);
        builder.Append(ParameterPrefix);
        builder.Append(name);
    }

    private static bool TryRenderLiteral(object? value, out string literal)
    {
        switch (value)
        {
            case null:
                literal = "null";
                return true;
            case string text:
                literal = "'" + text.Replace("'", "''") + "'";
                return true;
            case char character:
                literal = "'" + character.ToString().Replace("'", "''") + "'";
                return true;
            case bool boolean:
                literal = boolean ? "1" : "0";
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                literal = ((IFormattable)value).ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case float single when float.IsFinite(single):
                literal = single.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case double number when double.IsFinite(number):
                literal = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case DateTime dateTime:
                literal = "'" + dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "'";
                return true;
            case DateTimeOffset dateTimeOffset:
                literal = "'" + dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "'";
                return true;
            case DateOnly dateOnly:
                literal = "'" + dateOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "'";
                return true;
            case TimeOnly timeOnly:
                literal = "'" + timeOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "'";
                return true;
            case Guid guid:
                literal = "'" + guid.ToString("D") + "'";
                return true;
            default:
                var type = value.GetType();
                if (type.IsEnum)
                {
                    return TryRenderLiteral(Convert.ChangeType(value, Enum.GetUnderlyingType(type), System.Globalization.CultureInfo.InvariantCulture), out literal);
                }

                literal = string.Empty;
                return false;
        }
    }

    protected static string AddParameter(List<SqlParameter> parameters, string prefix, object? value)
    {
        var name = prefix + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parameters.Add(new SqlParameter(name, value));
        return name;
    }
}
