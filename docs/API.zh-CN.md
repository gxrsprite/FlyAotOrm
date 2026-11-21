# FlyAotOrm 中文 API 文档

FlyAotOrm 是一个面向 NativeAOT 的 typed SQL DSL 和源生成器组件。它不是 LINQ ORM，不提供运行时代理、变更跟踪、迁移、导航属性或懒加载。推荐把它当成“生成表/列元数据 + 显式 Query DSL + ADO.NET 执行”的轻量数据库访问层。

当前 Beta 验收边界见 [Beta 验收清单](BETA-CHECKLIST.zh-CN.md)。

## 基本使用流程

1. 用特性声明实体。
2. 编译后使用生成的 `Tables.{Entity}`、`{Entity}Crud`、`FlyDbContext`。
3. 用 `new FlyDb(connection, DbDialectKind.Xxx)` 创建查询入口。
4. 对复杂结果使用显式 DTO 投影。
5. 对手写 SQL 使用 `[DbRepository]` 接口。

## 实体与列

```csharp
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("created_at")]
    public DateTime CreatedAt { get; set; }
}
```

常用特性：

- `[DbTable("table_name")]`：声明数据库表名。
- `[DbColumn("column_name")]`：声明列名。
- `[DbKey]`：声明主键。默认 `IsIdentity = true`。
- `[DbProjection]`：显式要求为 DTO 生成 materializer。

生成内容：

- `{Entity}Metadata`
- `{Entity}Table : DbTable<TEntity>`
- `Tables.{Entity}`
- `{Entity}Crud`
- 实体 materializer
- `FlyDbContext.{Entities}` 查询入口

## 枚举存储

枚举默认按底层数字写入，兼容已有行为；需要保存枚举名称时，在列上指定存储方式：

```csharp
[DbColumn("status_code")]
public UserStatus StatusCode { get; set; }

[DbColumn("status_name", EnumStorage = DbEnumStorage.String)]
public UserStatus? StatusName { get; set; }
```

`DbEnumStorage.Number` 写入枚举底层数字；`DbEnumStorage.String` 写入名称（例如 `Active`）。可空枚举的 `null` 写入数据库 NULL。读取兼容数字、名称及数字字符串，名称区分大小写，无效名称会抛出异常；Flags 枚举支持 `Read, Write` 形式，未命名枚举值按数字文本保存，不使用 `Description` / `EnumMember` 的自定义名称。

生成的 CRUD（包括枚举主键）与列的值比较、`In`、`Between`、内联参数遵循该列配置；字符串列的排序和范围比较遵循数据库字符串排序规则，而非枚举数值顺序。两种存储方式对应的数据库列类型应分别使用数字与文本类型。

手写 Repository SQL 的参数没有实体列上下文，枚举参数仍默认按数字绑定；查询文本枚举列时使用字符串参数（如 `status.ToString()`）。实体、DTO、`SqlScalar` 的枚举结果均支持两种读取方式。修改列配置不会自动迁移已有数据库结构或数据。

## 创建 FlyDb

普通代码优先使用 `DbDialectKind`：

```csharp
var db = new FlyDb(connection, DbDialectKind.Sqlite);
```

支持的方言：

- `DbDialectKind.Sqlite`
- `DbDialectKind.PostgreSql`
- `DbDialectKind.MySql`
- `DbDialectKind.SqlServer`
- `DbDialectKind.NovaDb`（`NewLife.NovaDb.Client.NovaConnection`，详见 [NovaDb 适配说明](NovaDb.zh-CN.md))

只有需要自定义渲染器时才直接传 `ISqlRenderer`。如果只是想改 Query DSL 渲染参数名，用 `SqlRendererOptions`。

## 查询实体

```csharp
var u = Tables.User;

var rows = await db
    .Select(u)
    .Where(u.Name.StartsWith("a"))
    .OrderBy(u.Id.Desc())
    .Take(20)
    .ToListAsync();
```

常用方法：

- `Where(condition)`：追加 `where` 条件。重复调用会用 `and` 连接。
- `WhereIf(bool, condition)`：条件成立时追加 `where`。
- `And(condition)` / `AndIf(...)`
- `Or(condition)` / `OrIf(...)`
- `OrderBy(...)`
- `Take(count)`
- `Skip(count)`
- `Page(page, pageSize)`
- `FirstOrDefaultAsync()`
- `ToListAsync()`
- `CountAsync()`
- `AnyAsync()`

SQL Server 使用 `Take`、`Skip`、`Page`、`FirstOrDefaultAsync` 时必须先 `OrderBy`。

## 条件与列比较

```csharp
var users = await db
    .Select(Tables.User)
    .Where(Tables.User.Id.Eq(1))
    .And(Tables.User.Name.Contains("ann"))
    .ToListAsync();
```

常用条件：

- `Eq(value)` / `NotEq(value)`
- `GreaterThan(value)` / `GreaterOrEqual(value)`
- `LessThan(value)` / `LessOrEqual(value)`
- `Between(from, to)`
- `StartsWith(value)` / `Contains(value)` / `EndsWith(value)`
- `In(values)`
- `EqNull()` / `NotEqNull()`

同 CLR 类型的列支持列对列比较：

```csharp
orders.UserId.Eq(users.Id)
```

空值判断优先使用 `EqNull()` / `NotEqNull()`，不要依赖 `Eq(null)`。

## 标量查询

明确只查数量时用 `SelectCount`：

```csharp
var count = await db
    .SelectCount(Tables.User)
    .Where(Tables.User.Name.StartsWith("a"))
    .CountAsync();
```

明确只查是否存在时用 `SelectAny`：

```csharp
var exists = await db
    .SelectAny(Tables.User)
    .Where(Tables.User.Id.Eq(id))
    .AnyAsync();
```

`AnyAsync()` 会渲染 `exists` 形状，而不是用 `count(1)` 再判断。

## 查看 SQL

```csharp
var rendered = db
    .Select(Tables.User)
    .Where(Tables.User.Id.Eq(1))
    .ToSql();

Console.WriteLine(rendered.Sql);
```

可用入口：

- `ToSql()`：查看普通查询 SQL。
- `ToFirstSql()`：查看 `FirstOrDefaultAsync()` 使用的 SQL。
- `ToCountSql()`：查看 `CountAsync()` 使用的 SQL。
- `ToAnySql()`：查看 `AnyAsync()` 使用的 SQL。

`RenderedSql` 提供：

- `Sql`
- `Parameters`
- `TryGetParameter(name, out parameter)`
- `GetParameterValue(name)`
- `ToParameterDictionary()`

## 参数化与内联参数

默认情况下 Query DSL 使用 SQL 参数。

需要把安全字面量尽量拼进 SQL 时，可以使用：

```csharp
var sql = db
    .Select(Tables.User)
    .Where(Tables.User.Name.Eq("alice"))
    .InlineParameters()
    .ToSql();
```

别名：

- `NoneParameter()`
- `NoneParamter()`

这个能力只适合可信值或 SQL 检查。默认业务查询仍应使用参数化。

## 生成 CRUD

对有且只有一个 `[DbKey]` 的实体，生成器会生成 `{Entity}Crud`。

```csharp
await UserCrud.InsertAsync(connection, user);

var inserted = await UserCrud.InsertReturningAsync(connection, user);

var row = await UserCrud.GetByIdAsync(connection, id);

await UserCrud.UpdateAsync(connection, user);

await UserCrud.DeleteByIdAsync(connection, id);

var exists = await UserCrud.ExistsAsync(connection, id);

var count = await UserCrud.CountAsync(connection);

var rows = await UserCrud.ListAllAsync(connection);
```

默认 CRUD SQL 使用 SQLite/PostgreSQL 风格的双引号。需要其他方言时传 `DbDialectKind`：

```csharp
await UserCrud.InsertAsync(connection, user, DbDialectKind.SqlServer);
```

`InsertReturningAsync` 只支持 SQLite 和 PostgreSQL。NovaDb 请用 `InsertAsync` + `GetByIdAsync`（详见 [NovaDb 适配说明](NovaDb.zh-CN.md))。

## FlyDbContext

生成的 `FlyDbContext` 提供按实体命名的查询入口：

```csharp
var appDb = new FlyDbContext(db);

var users = await appDb.Users
    .Where(Tables.User.Name.StartsWith("a"))
    .ToListAsync();
```

`FlyDbContext` 的实体入口会自动应用 `[DbSoftDelete]`、`[DbTenant]`、`[DbDefaultFilter]`。

## 默认过滤

### 软删除

```csharp
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("is_deleted")]
    [DbSoftDelete]
    public bool IsDeleted { get; set; }
}
```

生成的 `FlyDbContext.Users` 和 repository `Query<User>` 会自动追加：

```csharp
Tables.User.IsDeleted.Eq(false)
```

### 租户

```csharp
[DbColumn("tenant_id")]
[DbTenant]
public long TenantId { get; set; }
```

使用前设置当前租户：

```csharp
using var tenant = FlyTenant.Use(10L);

var users = await new FlyDbContext(db).Users.ToListAsync();
```

### 自定义默认过滤

```csharp
[DbDefaultFilter(typeof(UserFilters), nameof(UserFilters.Active))]
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}

public static class UserFilters
{
    public static SqlCondition Active() => Tables.User.Status.Eq(1);
}
```

过滤方法必须是可访问的、`static`、无参数，并返回 `SqlCondition`。

## Join 与 DTO 投影

FlyAotOrm 不做导航属性和对象图组装。Join 结果应显式投影到 DTO。

```csharp
public sealed class UserOrderRow
{
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public long OrderId { get; set; }
    public decimal Amount { get; set; }
}

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
        o.Id.As(nameof(UserOrderRow.OrderId)),
        o.Amount.As(nameof(UserOrderRow.Amount)))
    .Where(o.Status.Eq(1))
    .ToListAsync();
```

左连接中可能缺失的 joined-side 字段，应在 DTO 中声明为可空属性。

## Repository

Repository 是手写 SQL 和 Query DSL 入口的语法糖，不是核心抽象。

```csharp
[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query();

    CountQuery<User> CountQuery();

    AnyQuery<User> AnyQuery();

    [SqlQuery("""
select id as Id, name as Name
from users
where id = @id
""")]
    Task<UserRow?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    [SqlExecute("""
update users
set name = @name
where id = @id
""")]
    Task<int> UpdateNameAsync(long id, string name);

    [SqlScalar("select count(1) from users")]
    Task<long> CountAsync();
}
```

Repository 规则：

- `Query<TEntity>` / `CountQuery<TEntity>` / `AnyQuery<TEntity>` 方法必须无参数。
- `TEntity` 必须是 `[DbTable]` 实体。
- `[SqlQuery]` 返回 DTO 或实体结果，必须能生成 materializer。
- 标量值使用 `[SqlScalar]`，不要用 `[SqlQuery]`。
- 修改语句使用 `[SqlExecute]`，返回 `Task<int>`。
- SQL 参数必须能在方法参数里找到同名参数。
- `CancellationToken` 会自动传给 ADO.NET async 方法，不要求出现在 SQL 里。

## 诊断

| 编号 | 含义 |
| --- | --- |
| `DORM001` | SQL 中的参数没有对应方法参数 |
| `DORM002` | 方法参数没有被 SQL 使用 |
| `DORM003` | 生成 CRUD 需要且只能有一个 `[DbKey]` |
| `DORM004` | Projection materializer 无法生成 |
| `DORM005` | 实体没有可更新列，因此不生成 `UpdateAsync` |
| `DORM006` | `[DbSoftDelete]` 必须标在 `bool` 属性上 |
| `DORM007` | `[DbDefaultFilter]` 指向的方法不合法 |
| `DORM008` | `[DbSoftDelete]` 或 `[DbTenant]` 重复声明 |
| `DORM009` | Repository Query DSL 入口签名不合法 |
| `DORM010` | `[SqlQuery]` 返回类型无法生成 materializer |
| `DORM011` | Repository 方法既没有 SQL 特性，也不是合法 Query DSL 入口 |
| `DORM012` | Repository 方法同时声明了多个 SQL 特性 |
| `DORM013` | Repository SQL 方法返回类型不支持 |

## 设计边界

FlyAotOrm 当前不提供：

- LINQ provider / `IQueryable`
- Expression Tree 查询
- 运行时模型构建
- 运行时 reader mapper
- change tracking
- navigation property
- lazy loading
- migrations
- runtime assembly scanning

这些边界是为了保持 NativeAOT 友好、生成代码可预测、运行时依赖少。
