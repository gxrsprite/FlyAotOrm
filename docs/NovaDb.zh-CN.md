# NovaDb 特性与适配说明

`DbDialectKind.NovaDb` 是 FlyAotOrm 的 NovaDb 方言，面向
[NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb)
（纯 C# 混合数据库：关系 + 时序 + MQ + KV 四引擎，嵌入/服务双模）。
实测基线：`NewLife.NovaDb 1.0.2026.901`，嵌入模式，11 轮真库探针
（WebdavSharp 切库实战 + 本仓 `NovaDbLiveTests` 锁定）。
上游缺口清单见 [WebdavSharp 的 docs/novadb.md](../../Server/WebdavSharp/docs/novadb.md)
（可直接拿去提 issue，都有最小复现）。

## 一句话画像

- **目录即库**：`Data Source` 是目录（不是单文件），每表一组
  `.data/.idx/.wal` 文件；拷目录即备份/迁移（停服拷，在线拷可能拿到 WAL 半截）。
- **纯托管零原生依赖**：arm64 / musl / 受限环境开箱即用，不需要
  SQLite 那套 `Batteries_V2.Init()`。
- **单库语义**：嵌入模式一个目录就是一个库，不支持 `库.表` 两段式，
  跨库查询不存在。
- **MVCC + Read Committed**：`BeginTransaction`/`Commit`/`Rollback` 照常用；
  WAL 默认 `NORMAL`（1 秒刷盘），`WalMode=Full` 最强持久性。
- **SQL 是常用子集**（约六成）：无视图/触发器/存储过程/窗口函数；
  无 `BIT` 类型；无自增元数据；`information_schema` 只有
  `TABLES/COLUMNS/STATISTICS` 三视图（`TABLE_SCHEMA` = 目录名）。

## 连接

```csharp
using NewLife.NovaDb.Client;

// 嵌入模式：Data Source 是目录，不存在会自动创建；
// 进程内同目录共享引擎，短连接关闭不丢数据。
using var conn = new NovaConnection { ConnectionString = "Data Source=./data/novadb" };
conn.Open();
var db = new FlyDb(conn, DbDialectKind.NovaDb);

// 网络模式：同一套 API（需独立 NovaServer 进程，默认端口 3306）
using var remote = new NovaConnection { ConnectionString = "Server=127.0.0.1;Port=3306;Database=mydb" };
```

## 类型映射（Nova 原生 9 类型）

| C# | Nova DDL（手写建表用） | 说明 |
| --- | --- | --- |
| `bool` | `BOOL` | .NET `bool` 直通；**不要用 `BIT`**（Nova 无此类型） |
| `int` / `long` | `INT` / `LONG`（`TINYINT/SMALLINT/MEDIUMINT/BIGINT` 等 MySQL 别名亦可） | 主键推荐应用侧赋值（`Guid.CreateVersion7()`），不要指望自增元数据 |
| `double` / `decimal` | `DOUBLE` / `DECIMAL` | 直接映射 |
| `string` | `STRING(n)` / `STRING`（`VARCHAR/CHAR/TEXT` 等别名亦可，长度不强制） | UTF-8；`Guid` 存 `STRING(36)`（小写 `D` 格式，`ReadGuid` 可回读） |
| `byte[]` | `BINARY(n)` / `BLOB` | 直接映射 |
| `DateTime` | `DATETIME` | 精度到 Ticks；ISO `O` 字面量（`2026-10-02T15:16:…`）已验证可读写 |
| 枚举 | 建议 `INT`（FlyAotOrm `ToDatabase` 即转底层整数） | ⚠️ 与 FreeSql 分支约定**不同**（它按 MySQL 语义存 `'Name'` 字符串）：同一列不要混用两个 ORM，见下 |
| `DateOnly`/`TimeOnly`/`DateTimeOffset` | 转 ISO `O` 字符串存取（与其它方言一致；`DateTime` 已真库验证，其余未逐一验证） | `Read` 侧均有对应回读 |

```sql
CREATE TABLE IF NOT EXISTS users (
    id      STRING(36) PRIMARY KEY,
    name    STRING(50) NOT NULL,
    age     INT DEFAULT 0,
    created DATETIME
);
```

手写 DDL 铁律（Nova 解析器硬限制，报错即 SQL 发不过去）：

1. 不要复合主键（`PRIMARY KEY (a,b)` 直接报解析错）：关联表用代理主键 + 唯一索引。
2. 不要表内建索引（`UNIQUE INDEX …` 写进 `CREATE TABLE` 会报错）：建完表再
   `CREATE [UNIQUE] INDEX x ON t(cols)`。
3. 不要 `库.表` 两段式（遇到 `.` 报错）；FlyAotOrm 生成 SQL 本来就不带库名。
4. `ALTER TABLE` 只支持 `ADD / MODIFY / DROP COLUMN / COMMENT`（无 `CHANGE / RENAME`）。
5. 自增（`AUTO_INCREMENT`）建表可写，但元数据查不到，不要依赖。

## 查询与 CRUD

```csharp
var rows = await db.Select(Tables.User)
    .Where(Tables.User.Name.StartsWith("a"))
    .OrderBy(Tables.User.Id.Desc())
    .Page(2, 25)
    .ToListAsync();

await UserCrud.InsertAsync(conn, user, DbDialectKind.NovaDb);
var one = await UserCrud.GetByIdAsync(conn, id, DbDialectKind.NovaDb);
```

生成 CRUD 已有 `NovaDbXxxSql` 常量并接入各 `GetXxxSql(dialect)` 开关
（反引号；`InsertReturningAsync` 仅 SQLite/PostgreSQL，Nova 请
`InsertAsync` + `GetByIdAsync`）。

### 渲染分化点（`NovaDbRenderer`，逐条真库验证）

| FlyAotOrm 发射 | Nova 行为 | 本仓做法 |
| --- | --- | --- |
| ``SELECT `a`.*`` | ❌ 限定星号解析报错 | 一律裸 `*` |
| `EXISTS (subquery)` | ❌ 无此关键字 | `1 IN (SELECT 1 …)`，1/0 语义不变 |
| `BETWEEN a AND b` | ❌ 词法有、解析器拒收 | 展开 `(… >= … AND … <= …)` |
| `LIMIT @p OFFSET @q` | ❌ LIMIT 后必须是整数字面量 | 永远内联整数 |
| 内联 `bool = 1` | ❌ 恒 false（引擎不做 bool↔数字隐转） | 内联 `TRUE`/`FALSE`；参数化 bool 走类型化比较，不受影响 |
| 参数名 `p0` 配 `@p0` 占位符 | ❌ `Parameter '@p0' not found` | `CommandParameters` 统一补 `@`（四库实测兼容） |

`JOIN`（`LEFT` 亦可）、`LIKE`（大小写不敏感）、`IN`（值列表/子查询）、
`IS NULL`、聚合（`COUNT/SUM/AVG/MIN/MAX`）、`CASE WHEN` 均直通。
`Skip` 必须配合 `Take`/`Page`（只有 skip 不产生分页 SQL，与其它方言一致）。

## 函数速查（DSL 相关，引擎原生支持）

`CONCAT / LENGTH（按字符）/ SUBSTRING / UPPER / LOWER / TRIM / REPLACE /
INSTR(haystack, needle) / IFNULL / COALESCE / NOW / YEAR / MONTH / DAY /
DATEADD / DATE_FORMAT / DATEDIFF`。没有 `LOCATE / CHAR_LENGTH /
UTC_TIMESTAMP / CURDATE`（DSL 不生成它们，不要手写绕过）。

## 跨 ORM 约定差异（重要）

- **枚举**：FlyAotOrm 存底层整数，WebdavSharp 的 FreeSql 分支存 `'Name'`
  字符串（MySQL `ENUM` 语义）。Nova 列本身只是 `INT`/`STRING`，
  读出来对不上——**同一张表的枚举列只允许一个 ORM 读写**。
- **注释**：FreeSql 分支会读写列/表 `COMMENT`（XML 文档也能流进去），
  FlyAotOrm 不做迁移、不碰注释，两边互不干扰。

## 已知限制

1. `InsertReturningAsync` 仅 SQLite/PostgreSQL（见上）。
2. 自增列：建表可写，`information_schema` 的 `EXTRA` 恒为空，不要指望内省。
3. 时序/MQ/KV 三引擎走 `NovaClient` RPC 或同库 `ENGINE=FLUX/KV` 表，
   不在 FlyAotOrm 关系路径内，需要时直写 SQL。
4. 中文 `Length` 按字符计（MySQL 按字节），语义以 Nova 为准。

## AOT 说明

NovaDb 纯托管，无原生依赖，`dotnet publish` 不需要原生库初始化。
`FlyDb` + 生成 CRUD/物化器路径保持 AOT 安全：固定 SQL 常量、
显式参数、生成物化器、ADO.NET 执行。
