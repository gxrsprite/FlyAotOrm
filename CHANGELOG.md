# 更新记录

## 1.0.0

首个正式版本，面向数据库访问相对直接、需要 NativeAOT 的工具类和小型应用。NuGet 是否上线以实际上传结果为准。

- 提供生成的实体/DTO 映射、固定 CRUD、typed Query DSL、Repository 和默认查询过滤器。
- 提供 SQLite、PostgreSQL、MySQL、SQL Server 和 NovaDb 方言渲染。
- 生成器以 Roslyn 4.8 为基线；包消费验证使用 .NET SDK 8.0.425。
- 本地验证：102 项测试通过，3 项外部数据库测试因未配置连接而明确跳过；Release 打包、纯 NuGet 安装执行和 win-x64 NativeAOT 编译运行通过。
- 外部测试连接改用 `FLYAOTORM_TEST_POSTGRES` / `FLYAOTORM_TEST_SQLSERVER` 环境变量，配置后连接或执行失败会使测试失败。必须使用专用、可清理的测试数据库。

### 边界

不提供 LINQ Provider、迁移、变更跟踪、导航属性或懒加载。方言 SQL 渲染覆盖不等同于所有数据库驱动的真实执行与 NativeAOT 验证；本次包消费与 AOT 冒烟使用 SQLite。安装包与源码受 MIT 许可。
