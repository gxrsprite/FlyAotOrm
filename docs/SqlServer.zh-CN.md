# SQL Server 适配说明

`DbDialectKind.SqlServer` 使用 `Microsoft.Data.SqlClient`（标准 ADO.NET
`DbConnection`）访问 SQL Server。方言渲染、生成 CRUD、Query DSL 与 `InsertReturningAsync`
全部支持，`InsertReturningAsync` 通过 `OUTPUT INSERTED` 一次往返返回主键与所有列。
实测基线：**Microsoft SQL Server 2025 (RTM, 17.0.1000.7)** + `Microsoft.Data.SqlClient 7.0.2`，
三 TFM（`net8.0` / `net10.0` / `net11.0`）真库测试通过。

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
| `DateOnly` / `TimeOnly` | `date` / `time` | 字符串 ISO 格式写入，读取兼容 `DateTime` / `TimeSpan` |
| `DateTimeOffset` | `datetimeoffset` | ISO 格式写入，`FlyDbValueConverter` 直读 |
| `Guid` | `uniqueidentifier` | 以 `D` 格式字符串写入，由服务端转换 |
| `byte[]` | `varbinary(max)` | 直接映射 |
| 枚举 | `int` 或 `nvarchar(n)` | 由 `EnumStorage` 决定，见 [API 文档](API.zh-CN.md) |

FlyAotOrm 不做迁移（non-goal），建表 DDL 手写。生成 CRUD 使用方括号常量，
不会读写注释、不改结构。

## 真库集成测试

测试用 `ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")` 控制：未设置环境变量时自动跳过，
设置后会连接真实实例，连接或断言失败即测试失败。必须使用**专用、可清理**的测试库。

由于 solution 会并行跑 `net8.0` / `net10.0` / `net11.0` 三个测试进程，测试会把连接串的
`InitialCatalog` 替换为各 TFM 独立的库 `flyaotorm_test_net8` / `_net10` / `_net11`，
避免并发 drop/create 同一张表。需预先建好这三个库：

```sql
IF DB_ID('flyaotorm_test_net8') IS NULL CREATE DATABASE flyaotorm_test_net8;
IF DB_ID('flyaotorm_test_net10') IS NULL CREATE DATABASE flyaotorm_test_net10;
IF DB_ID('flyaotorm_test_net11') IS NULL CREATE DATABASE flyaotorm_test_net11;
```

```powershell
$env:FLYAOTORM_TEST_SQLSERVER = "Server=localhost;Database=flyaotorm_test;User Id=sa;Password=...;TrustServerCertificate=True"
dotnet test tests/FlyAotOrm.Generator.Tests/FlyAotOrm.Generator.Tests.csproj
```

`SqlServerLiveTests` 覆盖：

1. `GeneratedCrudRoundTrips`：建表 → `InsertAsync` → `GetByIdAsync` → `UpdateAsync` → `ListAllAsync` / `ExistsAsync` / `CountAsync` / `DeleteByIdAsync`
2. `InsertReturning_IdentityAndAssignedKeys`：`OUTPUT INSERTED` 覆盖 identity 主键与 assigned 主键回读
3. `QuotedKeywordIdentifiersRoundTrip`：方括号转义保留字表名/列名（`[select]` / `[from]`）
4. `QueryDslPagingCountAnyAndJoinProjection`：`LIKE` + `OrderBy` + `Skip`/`Take`（`OFFSET/FETCH`）、`FirstOrDefaultAsync`、`CountAsync`、`AnyAsync`、`InlineParameters` 与 `Join` DTO 投影
5. `TypedValuesAndEnumStorageRoundTrip`：`uniqueidentifier`/`Guid`、`date`/`DateOnly`、`time`/`TimeOnly`、`datetimeoffset`/`DateTimeOffset` 与数字/字符串枚举存储
