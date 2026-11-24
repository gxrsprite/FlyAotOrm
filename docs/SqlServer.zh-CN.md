# SQL Server 适配说明

`DbDialectKind.SqlServer` 使用 `Microsoft.Data.SqlClient`（标准 ADO.NET
`DbConnection`）访问 SQL Server。方言渲染、生成 CRUD 与 Query DSL 全部支持，
`InsertReturningAsync` 通过 `OUTPUT INSERTED` 一次往返返回主键与所有列。

## 连接

```csharp
using Microsoft.Data.SqlClient;

using var connection = new SqlConnection("Server=localhost;Database=flyaotorm_test;User Id=sa;Password=...;TrustServerCertificate=True");
await connection.OpenAsync();
var db = new FlyDb(connection, DbDialectKind.SqlServer);

await UserCrud.InsertAsync(connection, user, DbDialectKind.SqlServer);
var inserted = await UserCrud.InsertReturningAsync(connection, user, DbDialectKind.SqlServer);
```

## 方言要点

| 项 | 行为 |
| --- | --- |
| 标识符 | `[name]` 方括号，内部 `]` 转义为 `]]` |
| 参数 | `@name`（与其它方言一致） |
| 分页 | `order by … offset @offset rows fetch next @limit rows only`；**必须先 `OrderBy`**，否则抛 `InvalidOperationException` |
| `AnyAsync` / `exists` | `case when exists (select 1 …) then 1 else 0 end` |
| `InsertReturningAsync` | `insert into [t] (…) output inserted.[col] as [Prop] … values (…)` |

`Take` / `Skip` / `Page` / `FirstOrDefaultAsync` 需要排序，这是 SQL Server
`OFFSET/FETCH` 的语法要求，不是 FlyAotOrm 限制。

## 类型映射

| C# | SQL Server 列 | 说明 |
| --- | --- | --- |
| `long` / `int` | `bigint` / `int` | 主键推荐 `identity(1,1)` + `[DbKey]`（生成器跳过 identity 列） |
| `bool` | `bit` | `.NET bool` 直通 |
| `string` | `nvarchar(n)` / `nvarchar(max)` | UTF-16，驱动自动处理 Unicode |
| `decimal` | `decimal(p,s)` | 直接映射 |
| `DateTime` | `datetime2` | `FlyDbValueConverter` 读取 `GetDateTime` |
| `Guid` | `uniqueidentifier` | 以 `D` 格式字符串写入，由服务端转换 |
| `byte[]` | `varbinary(max)` | 直接映射 |
| 枚举 | `int` 或 `nvarchar(n)` | 由 `EnumStorage` 决定，见 [API 文档](API.zh-CN.md) |

FlyAotOrm 不做迁移（non-goal），建表 DDL 手写。生成 CRUD 使用方括号常量，
不会读写注释、不改结构。

## 真库集成测试（你装好 SQL Server 后运行）

测试用 `ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")` 控制：未设置环境变量时自动跳过，
设置后会连接真实实例，连接或断言失败即测试失败。必须使用**专用、可清理**的测试库。

```powershell
$env:FLYAOTORM_TEST_SQLSERVER = "Server=localhost;Database=flyaotorm_test;User Id=sa;Password=...;TrustServerCertificate=True"
dotnet test tests/FlyAotOrm.Generator.Tests/FlyAotOrm.Generator.Tests.csproj
```

覆盖的测试：

- `QueryDslTests.ExecutesSqlServerQueryDslWhenLocalServerIsAvailable`：`LIKE` 前缀 + `OrderBy` + `Take`
- `GeneratedCrudIntegrationTests.GeneratedCrudExecutesAgainstSqlServerWhenLocalServerIsAvailable`：
  建表 → `InsertAsync` → `GetByIdAsync` → `UpdateAsync` → **`InsertReturningAsync`（OUTPUT INSERTED）**
  → `ListAllAsync` / `ExistsAsync` / `CountAsync` / `DeleteByIdAsync`，结束时删表

当前三 TFM 全量测试：`net8.0` / `net10.0` / `net11.0` 均通过（外部数据库用例按环境变量跳过）。
本地 SQL Server 尚未安装，装好后设置上述环境变量即可跑真库验证。
