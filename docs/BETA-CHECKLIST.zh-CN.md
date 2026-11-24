# FlyAotOrm Beta 验收清单

本文保留 Beta 阶段的历史验收范围；当前版本与发布验证见 [更新记录](../CHANGELOG.md)。

本文用于判断当前 FlyAotOrm 是否达到 Beta 完成状态。Beta 的含义不是生产稳定版，而是：核心 API 形状已经收口，NativeAOT 主链路可验证，错误用法有明确诊断，文档足够指导试用。

## 状态结论

当前状态：**Beta complete**。

适用范围：

- 小型 SQLite / NativeAOT 工具应用。
- 内部实验和 demo。
- 明确接受 API 仍可能小幅调整的早期试用。

不建议范围：

- 数据库模型是业务核心的大型系统。
- 需要 LINQ provider、change tracking、migration、navigation property 的项目。
- 对 ORM 稳定性有生产 SLA 要求的系统。

## API 收口

- [x] 统一常用入口为 `new FlyDb(connection, DbDialectKind.Xxx)`。
- [x] 保留 `ISqlRenderer` 作为高级扩展点。
- [x] 实体声明使用 `[DbTable]`、`[DbColumn]`、`[DbKey]`。
- [x] Query DSL 使用 typed table / column，不引入 LINQ provider。
- [x] Repository 只作为语法糖，不作为核心抽象。
- [x] 手写 SQL 分为 `[SqlQuery]`、`[SqlExecute]`、`[SqlScalar]`。
- [x] `Query<TEntity>`、`CountQuery<TEntity>`、`AnyQuery<TEntity>` 的 repository 入口已经收窄并有诊断保护。

## NativeAOT 主链路

- [x] 生成实体 metadata、table、column。
- [x] 生成实体 CRUD。
- [x] 生成实体 materializer。
- [x] 生成 DTO projection materializer。
- [x] Query DSL 执行使用 ADO.NET 命令和生成 materializer。
- [x] SQL repository 查询结果使用生成 materializer。
- [x] 标量读取走 `FlyDbValueConverter`。
- [x] Console sample 支持 NativeAOT publish 并能运行。

## Query DSL

- [x] `Select(table)`。
- [x] `SelectCount(table)`。
- [x] `SelectAny(table)`。
- [x] `Where` / `WhereIf`。
- [x] `And` / `AndIf`。
- [x] `Or` / `OrIf`。
- [x] `OrderBy`。
- [x] `Take` / `Skip` / `Page`。
- [x] `Join` / `LeftJoin`。
- [x] 多表 chained join。
- [x] 显式 DTO projection。
- [x] `ToSql` / `ToFirstSql` / `ToCountSql` / `ToAnySql`。
- [x] `ToListAsync` / `FirstOrDefaultAsync` / `CountAsync` / `AnyAsync`。
- [x] `InlineParameters` / `NoneParameter` / `NoneParamter`。

## 默认过滤

- [x] `[DbSoftDelete]`。
- [x] `[DbTenant]`。
- [x] `FlyTenant.Use(...)`。
- [x] `[DbDefaultFilter(typeof(Filters), nameof(Filters.Method))]`。
- [x] 默认过滤应用在 `FlyDbContext` 和 repository Query DSL 入口。
- [x] 手写 `[SqlQuery]` 不被隐式改写。

## 方言与渲染

- [x] SQLite。
- [x] PostgreSQL。
- [x] MySQL。
- [x] SQL Server。
- [x] SQL Server paging 必须有 `OrderBy`。
- [x] `SqlRendererOptions` 支持参数名前缀配置。
- [x] 生成 CRUD 具备不同方言的 quoted SQL 常量。

## 诊断

- [x] `DORM001` SQL 参数缺失。
- [x] `DORM002` 方法参数未被 SQL 使用。
- [x] `DORM003` CRUD 主键不合法。
- [x] `DORM004` Projection materializer 无法生成。
- [x] `DORM005` 没有可更新列。
- [x] `DORM006` `[DbSoftDelete]` 类型不合法。
- [x] `DORM007` 默认过滤方法不合法。
- [x] `DORM008` 默认过滤 marker 重复。
- [x] `DORM009` repository Query DSL 入口不合法。
- [x] `DORM010` repository query materializer 无法生成。
- [x] `DORM011` repository 方法不支持。
- [x] `DORM012` repository SQL 特性重复。
- [x] `DORM013` repository SQL 返回类型不支持。

## 文档

- [x] README 状态和能力说明。
- [x] 中文 API 文档：`docs/API.zh-CN.md`。
- [x] AI 使用指引：`SKILL.md`。
- [x] 长期方向：`长期提示词.md`。
- [x] Beta 验收清单：本文档。
- [x] 基础打包说明：`docs/PACKAGING.zh-CN.md`。

## 验证命令

Beta 完成前应保持以下命令通过：

```powershell
dotnet test FlyAotOrm.slnx --no-restore /p:UseSharedCompilation=false
dotnet build FlyAotOrm.slnx --no-restore /p:UseSharedCompilation=false
dotnet publish samples\Console\FlyAotOrm.ConsoleSample.csproj -c Release -f net8.0 -r win-x64 --no-restore /p:UseSharedCompilation=false
samples\Console\bin\Release\net8.0\win-x64\publish\FlyAotOrm.ConsoleSample.exe
:: 同样可对 net10.0 / net11.0 重复上面两条命令（替换 -f 与路径中的 TFM）
dotnet pack FlyAotOrm.slnx -c Release --no-restore /p:UseSharedCompilation=false
```

## 推迟到后续阶段

这些内容不阻塞 Beta：

- 固定 SQL 的 join DTO query 生成。
- 更深的 CRUD 与 renderer identifier policy 复用。
- upsert。
- 复杂方言特性。
- value object / JSON 等更完整类型扩展。
- 正式 NuGet 发布流程，包括签名、symbols package、SourceLink、外部发布和版本策略自动化。
