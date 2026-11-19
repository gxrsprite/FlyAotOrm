namespace FlyAotOrm;

public delegate TEntity DbRowMaterializer<out TEntity>(System.Data.Common.DbDataReader reader);

public abstract class DbTable
{
    protected DbTable(Type entityType, string tableName, string alias)
    {
        EntityType = entityType;
        TableName = tableName;
        Alias = alias;
    }

    public Type EntityType { get; }

    public string TableName { get; }

    public string Alias { get; }

    public TableNode ToNode() => new(EntityType, TableName, Alias);
}

public abstract class DbTable<TEntity> : DbTable
{
    protected DbTable(string tableName, string alias, DbRowMaterializer<TEntity> materializer)
        : base(typeof(TEntity), tableName, alias)
    {
        Materializer = materializer;
    }

    internal DbRowMaterializer<TEntity> Materializer { get; }
}
