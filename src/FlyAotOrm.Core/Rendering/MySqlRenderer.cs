namespace FlyAotOrm;

public sealed class MySqlRenderer : SqlRenderer
{
    public MySqlRenderer(SqlRendererOptions? options = null)
        : base(options)
    {
    }

    protected override string Quote(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";
}
