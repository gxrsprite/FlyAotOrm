# NovaDb 特性与适配说明

`DbDialectKind.NovaDb` 是 FlyAotOrm 的 NovaDb 方言，面向
[NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb)
（纯 C# 混合数据库：关系 + 时序 + MQ + KV 四引擎，嵌入/服务双模）。

实测基线：**`NewLife.NovaDb 1.0.2026.1121-beta0244`**（2026-11-21 构建），
嵌入模式。早期 901 基线的适配由 `NovaDbRenderer` 的兼容层保留，
当前基线由本仓 `NovaDbLiveTests`（真库）+ WebdavSharp 实战共同锁定。
上游变更与版本选择见 [WebdavSharp 的 docs/novadb.md](https://github.com/gxrsprite/WebdavSharpServer/blob/main/docs/novadb.md)。

> 版本选择：1024 稳定版不含 MySQL 方言兼容修复；1120-beta 包含 master 上
> 11/20 那批提交（`LIMIT m,n`、`库.表`、BIT、`LOCATE/CHAR_LENGTH`、
> `CHANGE COLUMN/RENAME TABLE`、内联索引、bool 0/1、规范类型名等）；
> **1121-beta0244 再加 `7b81b4d`：SELECT 真正走主键/二级索引 + LIMIT 下推**。
> 等上游出稳定版后换版本号重跑测试即可。

## 一句话画像

- **目录即库**：`Data Source` 是目录（不是单文件），每表一组
  `.data/.idx/.wal` 文件；拷目录即备份/迁移（停服拷，在线拷可能拿到 WAL 半截）。
- **纯托管零原生依赖**：arm64 / musl / 受限环境开箱即用，不需要
  SQLite 那套 `Batteries_V2.Init()`。
- **单库语义**：嵌入模式一个目录就是一个库；901 不支持 `库.表` 两段式，
  1120 起已兼容，但 FlyAotOrm 生成的 SQL 本来就不带库名。
- **MVCC + Read Committed**：`BeginTransaction`/`Commit`/`Rollback` 照常用；
  WAL 默认 `NORMAL`（1 秒刷盘），`WalMode=Full` 最强持久性。
- **SQL 是常用子集**（约六成）：无视图/触发器/存储过程/窗口函数；
  关系路径由 `NovaConnection` 走标准 ADO.NET。

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

## 类型映射

| C# | Nova DDL（手写建表用） | 说明 |
| --- | --- | --- |
| `bool` | `BOOL`（1120 起 `BIT(1)` 也可用） | .NET `bool` 直通；901 无 BIT，1120 起支持 |
| `int` / `long` | `INT` / `LONG`（MySQL 别名亦可） | 主键推荐应用侧赋值（`Guid.CreateVersion7()`） |
| `double` / `decimal` | `DOUBLE` / `DECIMAL` | 直接映射 |
| `string` | `STRING(n)` / `STRING`（`VARCHAR/CHAR/TEXT` 别名亦可） | UTF-8；**1120 起 `n` 是真实约束**，超长抛 `ConstraintViolation`（901 不强制） |
| `byte[]` | `BINARY(n)` / `BLOB` | 直接映射 |
| `DateTime` | `DATETIME` | 精度到 Ticks；ISO `O` 字面量（`2026-10-02T15:16:…`）已验证可读写 |
| 枚举 | 建议 `INT`（FlyAotOrm `ToDatabase` 即转底层整数） | ⚠️ 与 FreeSql 分支约定**不同**（它按 MySQL 语义存 `'Name'` 字符串）：同一列不要混用两个 ORM |
| `DateOnly`/`TimeOnly`/`DateTimeOffset` | 转 ISO `O` 字符串存取（与其它方言一致；`DateTime` 已真库验证，其余未逐一验证） | `Read` 侧均有对应回读 |

```sql
CREATE TABLE IF NOT EXISTS users (
    id      STRING(36) PRIMARY KEY,
    name    STRING(50) NOT NULL,   -- 1120 起最长 50，超长报错
    age     INT DEFAULT 0,
    created DATETIME
);
```

手写 DDL 铁律：

1. **复合主键仍不支持**（`PRIMARY KEY (a,b)` 明确报错）：关联表用代理主键 + 唯一索引。
2. 901 不支持表内 `UNIQUE INDEX` / `库.表` / `CHANGE COLUMN` / `RENAME TABLE`；
   **1120 起全部支持**。跨版本脚本按最低基线写即可。
3. 自增（`AUTO_INCREMENT`）建表可写；901 元数据查不到，1120 起
   `information_schema.Extra` 会输出 `auto_increment`（自增行为本身未逐一验证）。

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
（反引号；`InsertReturningAsync` 仅 SQLite/PostgreSQL/SQL Server/MySQL，Nova 请
`InsertAsync` + `GetByIdAsync`）。

### 渲染分化点（`NovaDbRenderer`，逐条真库验证）

| FlyAotOrm 发射 | 901 行为 | 1120 行为 | 本仓做法 |
| --- | --- | --- | --- |
| ``SELECT `a`.*`` | ❌ 限定星号解析报错 | 未重新验证 | 一律裸 `*` |
| `EXISTS (subquery)` | ❌ 无此关键字 | ✅ 支持 | `1 IN (SELECT 1 …)`，语义等价，跨版本保留 |
| `BETWEEN a AND b` | ❌ 词法有、解析器拒收 | 未重新验证 | 展开 `(… >= … AND … <= …)` |
| `LIMIT @p OFFSET @q` | ❌ LIMIT 后必须整数字面量 | 未重新验证 | 永远内联整数 |
| 内联 `bool = 1` | ❌ 恒 false | ✅ 按 0/1 语义 | 内联 `TRUE`/`FALSE`；参数化走类型化比较 |
| 内联字符串 `\`（如 `C:\new\test`） | 服务端不解码 | ⚠️ 服务端解码标准转义序列 | `InlineParameters()` 字面量 `\` → `\\`（`'` 仍 `''`）；参数化值不受影响 |
| 参数名 `p0` 配 `@p0` 占位符 | ❌ `Parameter '@p0' not found` | 兼容 | `CommandParameters` 统一补 `@`（四库实测兼容） |

`LIMIT`/`OFFSET` 永远渲染为整数字面量（`AppendPaging`）。这既是 901 的解析要求，
也是 1121 起 LIMIT 下推生效的前提：无排序/分组/聚合时引擎只反序列化需要的行。

901 的兼容层在 1120 上仍语义等价且真库通过，因此保留而不是切回标准 SQL：
同一渲染器覆盖两个基线。1120 新增的**反斜杠转义**必须处理，否则内联字面量
里的 `C:\new\test` 会被服务端解码成含 LF/TAB 的控制字符。

`JOIN`（`LEFT` 亦可）、`LIKE`（大小写不敏感）、`IN`（值列表/子查询）、
`IS NULL`、聚合（`COUNT/SUM/AVG/MIN/MAX`）、`CASE WHEN` 均直通。
`Skip` 必须配合 `Take`/`Page`（只有 skip 不产生分页 SQL，与其它方言一致）。

## SELECT 性能现状（1121-beta 起索引生效）

1120-beta 及之前，SQL 层所有查询都是全表扫描 + 内存 `WHERE` 过滤，二级索引只对写有意义。
1121-beta0244（上游 `7b81b4d`）已修复：

- 访问计划推导：`PK LOOKUP`（主键等值/IN）、`PK RANGE`（主键范围）、
  `INDEX LOOKUP`（单列二级索引等值/IN），其余 `FullScan`。
- 索引只缩小候选集，结果始终按完整 `WHERE` 复核；命中占比过高
  （`FullScanFallbackMinRows`）时回退全表扫描，避免低选择性索引反而变慢。
- **LIMIT 下推**：无排序/分组/聚合时只反序列化需要的行。
- 字符串索引键统一大写（与 SQL 大小写不敏感比较一致）；**索引文件版本升到 2，
  旧文件自动重建**（旧库升级无感）。
- `EXPLAIN` 与执行共用同一计划推导。本仓 `NovaDbLiveTests.Explain_PrimaryKeyLookup_ReportsPlan`
  锁定主键点查输出 `PK LOOKUP`，引擎升级若退回全扫会立刻可见。

FlyAotOrm 侧无需改 SQL 形态：`Where(...)` 命中索引列、`Take`/`Page` 内联 `LIMIT`
即可吃到索引与下推。没有索引的条件仍走全表扫描，大表分页建议给筛选列建索引。

## 函数速查（DSL 相关）

`CONCAT / LENGTH / SUBSTRING / UPPER / LOWER / TRIM / REPLACE /
INSTR / LOCATE / CHAR_LENGTH / IFNULL / COALESCE / NOW / YEAR / MONTH / DAY /
DATEADD / DATE_FORMAT / DATEDIFF`。901 缺少的 `LOCATE/CHAR_LENGTH/UTC_TIMESTAMP`
在 1120 起已支持；`DATE_ADD(col, INTERVAL n unit)`、`CURDATE()`、
三参数 `TIMESTAMPDIFF` 仍不支持（DSL 不生成它们，不要手写绕过）。

## 跨 ORM 约定差异（重要）

- **枚举**：FlyAotOrm 默认存底层整数；用
  `[DbColumn(..., EnumStorage = DbEnumStorage.String)]` 可改存枚举名称，
  与 WebdavSharp 的 FreeSql 分支（MySQL `ENUM` 语义存 `'Name'`）对齐。
  两种模式按列独立配置，读取兼容数字与名称；同一张表混用时逐列指定即可。
- **字符串长度**：1120 起 `STRING(n)` 是真约束，实体/DDL 里的长度两边必须一致；
  超长由服务端抛 `ConstraintViolation`。
- **注释**：FreeSql 分支会读写列/表 `COMMENT`，FlyAotOrm 不做迁移、不碰注释。

## 已知限制

1. `InsertReturningAsync` 仅 SQLite/PostgreSQL/SQL Server/MySQL（见上）。
2. 复合主键不支持（1120 明确报错，用代理主键 + 唯一索引）。
3. 时序/MQ/KV 三引擎走 `NovaClient` RPC 或同库 `ENGINE=FLUX/KV` 表，
   不在 FlyAotOrm 关系路径内，需要时直写 SQL。
4. `NovaConnection.DisposeSharedEngines()` 可在测试/短连接场景显式释放嵌入引擎。
5. 无索引条件的大表查询仍是全表扫描；1121 起索引文件版本为 2，旧库首次打开会自动重建索引。

## AOT 说明

NovaDb 纯托管，无原生依赖，`dotnet publish` 不需要原生库初始化。
`FlyDb` + 生成 CRUD/物化器路径保持 AOT 安全：固定 SQL 常量、
显式参数、生成物化器、ADO.NET 执行。
