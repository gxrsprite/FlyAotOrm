namespace FlyAotOrm;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DbTableAttribute : Attribute
{
    public DbTableAttribute(string name) => Name = name;

    public string Name { get; }

    public string? Schema { get; init; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbColumnAttribute : Attribute
{
    public DbColumnAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbKeyAttribute : Attribute
{
    public bool IsIdentity { get; init; } = true;
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbSoftDeleteAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbTenantAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class DbDefaultFilterAttribute : Attribute
{
    public DbDefaultFilterAttribute(Type providerType, string methodName)
    {
        ProviderType = providerType;
        MethodName = methodName;
    }

    public Type ProviderType { get; }

    public string MethodName { get; }
}

[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DbRepositoryAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DbProjectionAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class GenerateCrudAttribute : Attribute
{
    public GenerateCrudAttribute(Type entityType) => EntityType = entityType;

    public Type EntityType { get; }
}

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class GenerateCrudAttribute<TEntity> : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlQueryAttribute : Attribute
{
    public SqlQueryAttribute(string sql) => Sql = sql;

    public string Sql { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlExecuteAttribute : Attribute
{
    public SqlExecuteAttribute(string sql) => Sql = sql;

    public string Sql { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SqlScalarAttribute : Attribute
{
    public SqlScalarAttribute(string sql) => Sql = sql;

    public string Sql { get; }
}
