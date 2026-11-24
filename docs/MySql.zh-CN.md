# MySQL 适配说明

`DbDialectKind.MySql` 使用 `MySqlConnector`（标准 ADO.NET `DbConnection`）访问
MySQL / MariaDB。方言渲染、生成 CRUD、Query DSL 与 `InsertReturningAsync` 全部支持。
实测基线：**MySQL 8.4.9**（本地临时实例）+ `MySqlConnector 2.6.2`，三 TFM 真库测试通过。

## 连接

```csharp
using MySqlConnector;

using var connection = new MySqlConnection(
    "Server=127.0.0.1;Port=3306;Database=flyaotorm_test;User ID=test;Password=...;");
await connection.OpenAsync();
var db = new FlyDb(connection, DbDialectKind.MySql);

await UserCrud.InsertAsync(connection, user, DbDialectKind.MySql);
var inserted = await UserCrud.InsertReturningAsync(connection, user, DbDialectKind.MySql);
```

## 方言要点

| 项 | 行为 |
| --- | --- |
| 标识符 | \`name\` 反引号，内部 \` 加倍 |
| 参数 | `@name`（与其它方言一致） |
| 分页 | `LIMIT @limit OFFSET @offset` |
| `InsertReturningAsync` | INSERT + `SELECT ... WHERE id = LAST_INSERT_ID()` 两条语句合并为一个命令 |

`InsertReturningAsync` 依赖驱动**允许多语句**：MySqlConnector 默认允许；
若连接串显式关闭了多语句，该方法会失败。identity 主键用 `LAST_INSERT_ID()` 回读，
assigned 主键复用绑定的 `@Key` 参数，两种都真库验证过。

## 类型映射

| C# | MySQL 列 | 说明 |
| --- | --- | --- |
| `long` / `int` | `bigint` / `int` | 主键推荐 `AUTO_INCREMENT` + `[DbKey]`（生成器跳过 identity 列） |
| `bool` | `tinyint(1)` / `boolean` | 直接映射 |
| `string` | `varchar(n)` / `text` | 建议 `utf8mb4` 字符集 |
| `decimal` | `decimal(p,s)` | 直接映射 |
| `DateTime` | `datetime(6)` | 精度到微秒 |
| `Guid` | `char(36)` / `varchar(36)` | 以 `D` 格式字符串存取 |
| `byte[]` | `varbinary(n)` / `blob` | 直接映射 |
| 枚举 | `int` 或 `varchar(n)` | 由 `EnumStorage` 决定，见 [API 文档](API.zh-CN.md) |

FlyAotOrm 不做迁移（non-goal），建表 DDL 手写。生成 CRUD 使用反引号常量。

## 真库集成测试

测试用 `ExternalDatabaseFact("FLYAOTORM_TEST_MYSQL")`：未设置环境变量时跳过，
设置后连接真实实例，连接或断言失败即测试失败。必须使用**专用、可清理**的测试库。

由于 solution 会并行跑 net8.0/net10.0/net11.0 三个测试进程，测试会把连接串的
Database 替换为各 TFM 独立的库 `flyaotorm_test_net8` / `_net10` / `_net11`，
避免并发 drop/create 同一张表。需预先建好这三个库并给测试用户授权：

```sql
CREATE DATABASE IF NOT EXISTS flyaotorm_test_net8 CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS flyaotorm_test_net10 CHARACTER SET utf8mb4;
CREATE DATABASE IF NOT EXISTS flyaotorm_test_net11 CHARACTER SET utf8mb4;
GRANT ALL ON flyaotorm_test_net8.* TO 'test'@'127.0.0.1';
GRANT ALL ON flyaotorm_test_net10.* TO 'test'@'127.0.0.1';
GRANT ALL ON flyaotorm_test_net11.* TO 'test'@'127.0.0.1';
```

```powershell
$env:FLYAOTORM_TEST_MYSQL = "Server=127.0.0.1;Port=3306;Database=flyaotorm_test;User ID=test;Password=...;"
dotnet test tests/FlyAotOrm.Generator.Tests/FlyAotOrm.Generator.Tests.csproj
```

`MySqlLiveTests` 覆盖：

1. `ConnectivityAndMultiStatementLastInsertId`：连接、多语句、`LAST_INSERT_ID()`
2. `GeneratedCrudRoundTrips`：建表 → `InsertAsync` → `GetByIdAsync` → `UpdateAsync` → `ListAllAsync` / `ExistsAsync` / `CountAsync` / `DeleteByIdAsync`
3. `InsertReturning_IdentityAndAssignedKeys`：identity 主键(`LAST_INSERT_ID()`)与 assigned 主键(`@Id`)两种回读

本地实测使用免界面部署：MySQL 8.4 ZIP/MSI 静默安装、`mysqld --initialize-insecure`
初始化独立 data 目录、`--init-file` 创建专用测试用户，端口 3307（避开本机 MariaDB 的 3306）。
