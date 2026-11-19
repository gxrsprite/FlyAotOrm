using System.Text;

namespace FlyAotOrm;

public sealed class SqlServerRenderer : SqlRenderer
{
    public SqlServerRenderer(SqlRendererOptions? options = null)
        : base(options)
    {
    }

    protected override string Quote(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    protected override void AppendPaging(StringBuilder builder, SelectNode node, List<SqlParameter> parameters)
    {
        if (node.Limit is null)
        {
            return;
        }

        if (node.Orders.Count == 0)
        {
            throw new InvalidOperationException("SQL Server paging requires at least one OrderBy column.");
        }

        if (node.Offset is null)
        {
            builder.Append(" offset 0 rows");
        }
        else
        {
            builder.Append(" offset ");
            AppendPagingValue(builder, parameters, "offset", node.Offset.Value, node.InlineParameters);
            builder.Append(" rows");
        }

        builder.Append(" fetch next ");
        AppendPagingValue(builder, parameters, "limit", node.Limit.Value, node.InlineParameters);
        builder.Append(" rows only");
    }
}
