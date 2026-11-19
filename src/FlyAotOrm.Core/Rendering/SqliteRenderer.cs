namespace FlyAotOrm;

public sealed class SqliteRenderer : SqlRenderer
{
    public SqliteRenderer(SqlRendererOptions? options = null)
        : base(options)
    {
    }

    protected override string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
