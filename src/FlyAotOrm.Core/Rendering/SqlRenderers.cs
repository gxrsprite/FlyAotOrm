namespace FlyAotOrm;

public static class SqlRenderers
{
    public static ISqlRenderer Create(DbDialectKind dialect, SqlRendererOptions? options = null)
        => dialect switch
        {
            DbDialectKind.PostgreSql => new PostgreSqlRenderer(options),
            DbDialectKind.SqlServer => new SqlServerRenderer(options),
            DbDialectKind.MySql => new MySqlRenderer(options),
            DbDialectKind.NovaDb => new NovaDbRenderer(options),
            DbDialectKind.Sqlite => new SqliteRenderer(options),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported SQL dialect.")
        };
}
