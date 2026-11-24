# FlyAotOrm Skill

Use this skill when building or modifying code that uses FlyAotOrm.

FlyAotOrm is a NativeAOT-first typed SQL DSL over ADO.NET. It is not a LINQ ORM and should not be shaped like EF Core, SqlSugar, RepoDB, or a runtime mapper.

## Use The Component This Way

Define entities with attributes:

```csharp
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";
}
```

Use `[DbSoftDelete]` on a `bool` deletion flag when generated `FlyDbContext` and repository `Query<TEntity>` entry points should exclude deleted rows by default. Handwritten `[SqlQuery]` SQL is not rewritten.

Enums use numeric storage by default. Use `[DbColumn("status", EnumStorage = DbEnumStorage.String)]` for enum-name storage. Generated CRUD and column predicates follow this setting; reads accept both numeric values and case-sensitive names, including nullable enums. Handwritten SQL parameters have no column context: pass a string for a text enum column. Changing storage does not migrate existing data.

Use `[DbTenant]` on a tenant id column when generated query entry points should filter by the current tenant:

```csharp
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("tenant_id")]
    [DbTenant]
    public long TenantId { get; set; }
}

using var tenant = FlyTenant.Use(10L);
var rows = await new FlyDbContext(db).Users.ToListAsync();
```

The tenant value is required before accessing a generated `[DbTenant]` query entry point.

Use `[DbDefaultFilter(typeof(Filters), nameof(Filters.Method))]` when an entity needs a static default predicate on generated query entry points:

```csharp
[DbDefaultFilter(typeof(UserFilters), nameof(UserFilters.Active))]
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("status")]
    public int Status { get; set; }
}

public static class UserFilters
{
    public static SqlCondition Active() => Tables.User.Status.Eq(1);
}
```

The filter method must be accessible, static, parameterless, and return `SqlCondition`.

Use generated tables and typed columns:

```csharp
var db = new FlyDb(connection, DbDialectKind.Sqlite);
var u = Tables.User;

var rows = await db
    .Select(u)
    .Where(u.Id.Eq(id))
    .OrderBy(u.Id.Desc())
    .Take(20)
    .ToListAsync();
```

Use `SelectCount(table)` when the query is explicitly scalar count:

```csharp
var total = await db
    .SelectCount(Tables.User)
    .Where(Tables.User.Name.StartsWith("a"))
    .CountAsync();
```

Use `SelectAny(table)` when the query is explicitly scalar existence:

```csharp
var exists = await db
    .SelectAny(Tables.User)
    .Where(Tables.User.Id.Eq(id))
    .AnyAsync();
```

When a generated context is available, prefer it for discoverable entity query entry points:

```csharp
var appDb = new FlyDbContext(db);
var rows = await appDb.Users
    .Where(Tables.User.Id.Eq(id))
    .ToListAsync();
```

Use `ToSql()` when inspecting generated SQL, `ToFirstSql()` for `FirstOrDefaultAsync`, `ToCountSql()` for `CountAsync`, and `ToAnySql()` for `AnyAsync`. `RenderedSql` keeps ordered parameters and also supports `GetParameterValue("p0")`, `GetParameterValue("@p0")`, `TryGetParameter`, and `ToParameterDictionary`.

Use `WhereIf`, `AndIf`, and `OrIf` for optional filters instead of manually branching query construction.

Repeated `Where(...)` calls append with `and`; use `Or(...)` explicitly when the next condition should be disjunctive.

Use `InlineParameters()` when a query should inline safe literal values instead of using SQL parameters. `NoneParameter()` and `NoneParamter()` are aliases. Treat this as opt-in for trusted values or SQL inspection; default queries must remain parameterized.

Use `new FlyDb(connection, DbDialectKind.Xxx)` for normal code. Pass `SqlRendererOptions` only when rendered Query DSL parameter names need custom prefixes. Pass a custom `ISqlRenderer` only for advanced renderer experiments.

Generated columns expose `IsNullable`; generated metadata exposes `NullableColumnNames` and `{Property}IsNullable`.

Generated materializers, generated CRUD parameter binding, and Query DSL execution parameter binding use `FlyDbValueConverter`; preserve that shared conversion path when adding scalar type support.

Use generated CRUD for fixed entity operations:

```csharp
await UserCrud.InsertAsync(connection, user);
var inserted = await UserCrud.InsertReturningAsync(connection, user);
var user = await UserCrud.GetByIdAsync(connection, id);
await UserCrud.UpdateAsync(connection, user);
await UserCrud.DeleteByIdAsync(connection, id);
```

Default generated CRUD uses SQLite/PostgreSQL-style quoted identifiers. Pass `DbDialectKind` when SQL Server brackets, MySQL backticks, or NovaDb backticks are needed:

```csharp
await UserCrud.InsertAsync(connection, user, DbDialectKind.SqlServer);
await UserCrud.InsertAsync(novaConnection, user, DbDialectKind.NovaDb);
```

`InsertReturningAsync` is generated for SQLite, PostgreSQL and SQL Server (via `OUTPUT INSERTED`). NovaDb and MySQL do not support it; use `InsertAsync` followed by `GetByIdAsync` with an application-assigned key.

Generated `ExistsAsync` should use an `exists` SQL shape instead of `count(1)` when only existence is needed.

Use explicit DTO projection for joins:

```csharp
var u = Tables.User;
var o = Tables.Order;

var rows = await db
    .Select<UserOrderRow>()
    .From(u)
    .LeftJoin(o)
    .On(u.Id.Eq(o.UserId))
    .Columns(
        u.Id.As(nameof(UserOrderRow.UserId)),
        u.Name.As(nameof(UserOrderRow.UserName)),
        o.Id.As(nameof(UserOrderRow.OrderId)))
    .ToListAsync();
```

Column-to-column comparisons are supported for `Eq`, `NotEq`, `GreaterThan`, `GreaterOrEqual`, `LessThan`, and `LessOrEqual` when both columns have the same CLR type.

Chain additional `Join(...).On(...)` or `LeftJoin(...).On(...)` calls from the returned projection query when a DTO needs more than two tables.

Use `EqNull()` and `NotEqNull()` for null checks. Direct `Eq(null)` can be ambiguous for nullable reference columns because column-to-column overloads also exist.

For left joins, make joined-side DTO properties nullable when missing rows matter.

Use SQL attribute repositories only as sugar for handwritten SQL:

```csharp
[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query();

    [SqlQuery("select id as Id, name as Name from users where id = @id")]
    Task<UserRow?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    [SqlScalar("select count(1) from users")]
    Task<long> CountAsync();
}
```

Declare `Query<TEntity> Query()` on a repository interface when the repository should expose the generated entity Query DSL using the same connection and dialect.

Declare no-argument `CountQuery<TEntity>` or `AnyQuery<TEntity>` repository methods when the repository should expose scalar count/existence Query DSL entry points with the same default filters.

## Design Boundaries

- Keep core projects free of Dapper and DapperAOT dependencies.
- Keep runtime mapping out of AOT-sensitive paths.
- Prefer generated fixed SQL, explicit parameters, and generated materializers.
- Use normal C# method-based DSL calls, not expression trees.
- Model joins as explicit DTO projections, not navigation properties.
- Keep repository APIs as convenience sugar, not the core abstraction.
- Pass declared `CancellationToken` values through to ADO.NET async calls.
- For SQL Server paging, require `OrderBy` before `Take`, `Skip`, `Page`, or `FirstOrDefaultAsync`.

Do not introduce:

- LINQ provider or `IQueryable`
- `Expression<Func<...>>`
- runtime model builder
- runtime reader materializer
- runtime proxy
- lazy loading
- navigation property graph assembly
- change tracking
- migrations
- runtime assembly scanning

## Generated Shape To Preserve

For each `[DbTable]` entity with exactly one `[DbKey]`, the generator emits:

- `{Entity}Metadata`
- `{Entity}Table : DbTable<TEntity>`
- `Tables.{Entity}`
- `{Entity}Crud`
- generated entity materializer

Projection DTO materializers come from:

- `Select<TProjection>()` discovery for visible public/internal DTOs
- explicit `[DbProjection]`
- SQL repository query return types when the generator can see public settable properties

Unsupported projection DTOs should fail with a clear diagnostic, not fall back to reflection.

## Diagnostics

- `DORM001`: SQL parameter missing from repository method
- `DORM002`: method parameter unused by SQL
- `DORM003`: CRUD generation requires exactly one `[DbKey]`
- `DORM004`: projection materializer cannot be generated
- `DORM005`: CRUD update cannot be generated because the entity has no updateable columns
- `DORM006`: `[DbSoftDelete]` must be used on a `bool` property
- `DORM007`: `[DbDefaultFilter]` must point to an accessible static parameterless method returning `SqlCondition`
- `DORM008`: an entity must not declare more than one `[DbSoftDelete]` or `[DbTenant]` property
- `DORM009`: repository Query DSL entry points must be parameterless and use a `[DbTable]` entity type
- `DORM010`: `[SqlQuery]` return types must support generated materialization; use `[SqlScalar]` for scalar values
- `DORM011`: repository methods must declare a SQL attribute or be a supported Query DSL entry point
- `DORM012`: repository methods must not declare more than one SQL attribute
- `DORM013`: repository SQL methods must return a supported `Task<T>` shape
