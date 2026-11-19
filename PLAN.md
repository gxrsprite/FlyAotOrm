# FlyAotOrm 开发计划

## 当前状态

Beta 功能范围已完成，正在准备首个正式版 `1.0.0`。发布与验证结果见 [更新记录](CHANGELOG.md)，NuGet 上线须以实际上传结果为准。实体与 DTO 映射、CRUD、Query DSL、Repository、默认查询过滤器和方言渲染已有实现，详细用法统一见 [API 文档](docs/API.zh-CN.md)。

已有生成代码快照、渲染矩阵、SQLite 集成测试和 NativeAOT 示例；外部数据库实际执行覆盖仍需与纯 SQL 渲染覆盖区分。Beta 验收范围见 [验收清单](docs/BETA-CHECKLIST.zh-CN.md)。

## 发布前

- [x] 确认使用 MIT，补齐 `LICENSE` 与版权信息。
- [x] 集成测试改用环境变量，明确跳过与失败；README 不再包含本机账号。
- [x] 生成器以 Roslyn 4.8 为基线，通过 .NET SDK 8.0.425 包消费验证。
- [x] 独立项目从本地 NuGet 包安装，验证生成器、SQLite CRUD 和 win-x64 NativeAOT 发布运行。
- [ ] 配置 GitHub CI，覆盖构建、测试、包消费和 NativeAOT 冒烟验证。
- [x] 补齐安装说明与版本更新记录，生成 `1.0.0` 正式包。
- [ ] 获得 NuGet 发布授权并上传三个正式包，验证线上安装。

## 后续候选

按实际使用反馈决定，不作为首个预览包的前置条件：

- 扩展数据库驱动实测与值类型转换覆盖。
- 完善生成器对不支持的类型和方法形状的诊断。
- 评估固定 SQL Join DTO、标识符策略复用和 upsert。
- 完善 SourceLink、符号包和发布自动化。

## 开发约束

保持 API 精简，Repository 只是 DSL / 生成执行的便利入口。不引入 LINQ Provider、运行时反射映射或表达式编译；不做变更跟踪、迁移和懒加载。公开 API 变更同步更新 API 文档与 `SKILL.md`，核心行为变更补回归测试。
