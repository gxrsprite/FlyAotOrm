namespace FlyAotOrm;

public sealed class PostgreSqlRenderer : SqlRenderer
{
    public PostgreSqlRenderer(SqlRendererOptions? options = null)
        : base(options)
    {
    }

    protected override string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
