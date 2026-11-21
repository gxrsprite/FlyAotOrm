using System.Text;

namespace FlyAotOrm;

/// <summary>
/// NovaDb renderer. NovaDb speaks a MySQL-shaped SQL subset over
/// <c>NewLife.NovaDb.Client.NovaConnection</c> (standard ADO.NET
/// <c>DbConnection</c>): <c>@name</c> parameters and backtick identifiers.
/// A dedicated renderer (rather than reusing <see cref="MySqlRenderer"/>)
/// keeps the Nova-specific divergences below greppable as the engines evolve.
/// Verified against live NovaDb 1.0.2026.901, 1.0.2026.1120-beta1409 and
/// 1.0.2026.1121-beta0244 (embedded); see <c>docs/NovaDb.zh-CN.md</c>.
/// The 901 workarounds are kept: 1120 fixed most engine gaps, but these shapes
/// remain semantically equivalent and keep the renderer working across all three
/// baselines. 1120 made backslash escapes live on the server, so inline string
/// literals double backslashes (see <see cref="TryRenderInlineLiteral"/>).
/// 1121 added index/LIMIT pushdown; inlining LIMIT/OFFSET as integer literals
/// (see <see cref="AppendPaging"/>) is what lets the engine push the limit down.
/// </summary>
public sealed class NovaDbRenderer : SqlRenderer
{
    public NovaDbRenderer(SqlRendererOptions? options = null)
        : base(options)
    {
    }

    protected override string Quote(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    /// <summary>NovaDb rejects a qualified star (<c>`a`.*</c>): always render bare <c>*</c>.</summary>
    protected override string RenderSelectAllColumns(SelectNode node)
        => "*";

    /// <summary>
    /// NovaDb has no <c>EXISTS (subquery)</c> keyword. <c>1 IN (SELECT 1 …)</c>
    /// evaluates identically through its <c>IN</c>-subquery support and keeps the 1/0 shape.
    /// </summary>
    protected override string RenderExistenceCheck(SelectNode node, List<SqlParameter> parameters)
    {
        var builder = new StringBuilder();
        builder.Append("1 in (select 1 from ");
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

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>
    /// NovaDb lexes <c>BETWEEN</c> but its expression parser rejects it:
    /// expand to <c>(… &gt;= … AND … &lt;= …)</c>.
    /// </summary>
    protected override string RenderBetweenCondition(BetweenCondition between, List<SqlParameter> parameters, bool inlineParameters)
        => "(" + RenderExpression(between.Left, parameters, inlineParameters)
        + " >= " + RenderValue(between.Lower, parameters, inlineParameters)
        + " and " + RenderExpression(between.Left, parameters, inlineParameters)
        + " <= " + RenderValue(between.Upper, parameters, inlineParameters) + ")";

    /// <summary>
    /// NovaDb requires <c>LIMIT</c>/<c>OFFSET</c> to be integer literals:
    /// a parameter (<c>LIMIT @p</c>) is a parse error. Always inline them.
    /// </summary>
    protected override void AppendPaging(StringBuilder builder, SelectNode node, List<SqlParameter> parameters)
    {
        if (node.Limit is null)
        {
            return;
        }

        builder.Append(" limit ");
        builder.Append(node.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (node.Offset is not null)
        {
            builder.Append(" offset ");
            builder.Append(node.Offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// NovaDb never coerces <c>bool = 1</c> (always false), so inline booleans
    /// must be <c>TRUE</c>/<c>FALSE</c> keywords. Parameterized booleans are unaffected
    /// (the driver compares typed values).
    /// </summary>
    protected override bool TryRenderInlineLiteral(object? value, out string literal)
    {
        switch (value)
        {
            case bool boolean:
                literal = boolean ? "TRUE" : "FALSE";
                return true;
            case string text:
                // 1120-beta onwards Nova decodes backslash escapes on the server.
                // Without doubling, @"C:\new\test" arrives with LF/TAB control chars.
                // Single quotes stay doubled via the standard SQL rule.
                literal = "'" + text.Replace("\\", "\\\\").Replace("'", "''") + "'";
                return true;
            case char character:
                literal = "'" + character.ToString().Replace("\\", "\\\\").Replace("'", "''") + "'";
                return true;
            default:
                literal = string.Empty;
                return false;
        }
    }
}
