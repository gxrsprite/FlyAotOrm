# FlyAotOrm

面向 NativeAOT 和 AI 辅助开发的轻量 ORM：typed SQL DSL、源生成器和 ADO.NET 执行层。

开源仓库：[gxrsprite/FlyAotOrm](https://github.com/gxrsprite/FlyAotOrm) · 许可证：[MIT](LICENSE)

## 项目定位

FlyAotOrm 优先服务 NativeAOT，不追求传统 C# ORM 的 Expression Tree / LINQ 写法。它的 API 更接近显式的 table / column / query builder，目标是可预测的生成代码、可检查的 SQL，以及不依赖运行时反射映射和表达式编译的执行路径。

### AI 开发 + NativeAOT

既然大量查询代码由 AI 编写，API 是否足够简短、是否完全符合传统 C# ORM 的审美，就不再是最重要的取舍。代码可能难看一点点、啰嗦一点点，但显式的表、列、条件和 DTO 投影也让查询结构更容易检查。

让 AI 多写一点明确的代码，开发者直接检查生成代码和 SQL，再通过编译诊断、测试和实际 NativeAOT 发布验证结果。这种取舍很适合以 AI 辅助开发为主、数据库访问相对直接、需要 AOT 编译的工具类和小型应用。

AI 写代码不意味着可以省略审查和测试。FlyAotOrm 不面向复杂数据库模型驱动的大型业务系统，不提供 LINQ Provider、变更跟踪、迁移、运行时代理、导航属性或懒加载。

### English

FlyAotOrm is a lightweight, NativeAOT-first ORM with a typed SQL DSL, source-generated materializers, and ADO.NET execution. It favors explicit tables, columns, conditions, and DTO projections over LINQ expressions and runtime reflection mapping.

For projects developed primarily with AI assistance, a little extra verbosity or less conventional C# syntax is a reasonable tradeoff for inspectable SQL and predictable generated code. It is a good fit for tools and small applications with straightforward database access and NativeAOT delivery requirements. AI-generated code still needs review and testing.

It is not intended for large database-centric business systems and does not provide change tracking, migrations, runtime proxies, navigation properties, or lazy loading.

## 核心能力

- 通过 `[DbTable]`、`[DbColumn]`、`[DbKey]` 生成表、列、实体映射和固定 CRUD。
- Typed Query DSL：条件、排序、分页、Count / Any，以及显式 DTO Join 投影。
- 生成 `FlyDbContext` 和 Repository，支持软删除、租户及自定义默认查询过滤器。
- 枚举列默认按底层数字存储；`[DbColumn("col", EnumStorage = DbEnumStorage.String)]` 改为名称存储，生成 CRUD 与查询条件按列配置执行，读取兼容数字与名称。
- SQL 与参数可检查；默认参数化，可用 `InlineParameters()` 按支持的类型内联值。
- 提供 SQLite、PostgreSQL、MySQL、SQL Server 和 NovaDb 方言；具体用法及限制见 API 文档。
- 无 Dapper / DapperAOT 依赖；无法生成映射时提供编译诊断，不回退到运行时反射映射。

执行链路：

```text
Entity -> Source Generator -> Table/Column -> Query DSL -> SQL AST -> Renderer -> ADO.NET -> Materializer
```

## 安装

目标框架为 .NET 8 或更高，生成器支持 .NET 8 SDK 的 Roslyn 4.8 及以上编译器。NuGet 发布完成后，在应用项目中引用：

```xml
<PackageReference Include="FlyAotOrm.Core" Version="1.0.0" />
<PackageReference Include="FlyAotOrm.Generator" Version="1.0.0" PrivateAssets="all" />
```

Core 会传递引用 Abstractions。数据库驱动由应用自行安装，驱动的 NativeAOT 兼容性需要单独确认。

## 查询示例

实体声明：

```csharp
using FlyAotOrm;

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

使用已打开的数据库连接查询：

```csharp
var db = new FlyDb(connection, DbDialectKind.Sqlite);
var u = Tables.User;

var rows = await db
    .Select(u)
    .Where(u.Name.StartsWith("a"))
    .OrderBy(u.Id.Asc())
    .Take(20)
    .ToListAsync();
```

数据库驱动由应用选择。Join 使用显式 DTO 投影，不组装导航属性对象图；Left Join 的可缺失列应使用可空 DTO 属性。SQL Server 分页需要先指定 `OrderBy`。

## 文档与示例

- [中文 API 文档](docs/API.zh-CN.md)
- [AI 使用指引](SKILL.md)
- [控制台示例](samples/Console/Program.cs)
- [NovaDb 适配说明](docs/NovaDb.zh-CN.md)
- [打包说明](docs/PACKAGING.zh-CN.md)
- [更新记录](CHANGELOG.md)
- [开发计划与发布准备](PLAN.md)

源码分为 `FlyAotOrm.Abstractions`（特性与契约）、`FlyAotOrm.Core`（DSL 与执行层）、`FlyAotOrm.Generator`（源生成器），另含 `samples` 和 `tests`。
