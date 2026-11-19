# FlyAotOrm 打包说明

首个正式版本为 `1.0.0`，面向 README 所述的小型 NativeAOT 应用；正式版本号不代表所有数据库驱动都已完成生产实测。

开源仓库：[gxrsprite/FlyAotOrm](https://github.com/gxrsprite/FlyAotOrm)，许可证：[MIT](../LICENSE)。包元数据已包含仓库与项目主页地址。

## 注册 NuGet 账号

1. 打开 [NuGet.org 登录页](https://www.nuget.org/users/account/LogOn)，使用个人 Microsoft 账号登录；没有 Microsoft 账号时先按页面入口创建。
2. 首次登录后按提示设置 NuGet 用户名、确认邮箱并完成账号验证。
3. 手动网页上传不需要 API Key；命令行发布需要在账号的 [API Keys](https://www.nuget.org/account/apikeys) 页面创建密钥，授权发布新包和新版本，包名范围限制为 `FlyAotOrm.*`，并设置合理有效期。

API Key 不要提交到 Git、写入文档或发送到聊天。NuGet 账号和 GitHub 仓库是分别管理的。

## 首次发布

先完成 [发布前待办](../PLAN.md)，在项目根目录执行：

```powershell
dotnet test FlyAotOrm.slnx /p:UseSharedCompilation=false
dotnet pack FlyAotOrm.slnx -c Release /p:UseSharedCompilation=false
```

确认命令成功后，在 [NuGet 上传页](https://www.nuget.org/packages/manage/upload) 依次上传 `artifacts/packages` 中的 Abstractions、Core、Generator 三个 `.nupkg`，核对包信息后确认发布。

也可以在自己的终端安全设置 `NUGET_API_KEY` 环境变量后，用 PowerShell 发布：

```powershell
if (-not $env:NUGET_API_KEY) { throw "NUGET_API_KEY is required." }
foreach ($name in 'Abstractions', 'Core', 'Generator') {
    dotnet nuget push "artifacts/packages/FlyAotOrm.$name.1.0.0.nupkg" --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw "Package push failed: $name" }
}
```

包 ID 必须可用或由你拥有；同一 ID 和版本发布后不能覆盖。后续修改应统一提升 `Directory.Build.props` 中的版本，并同步发布命令与安装示例。发布后等待 NuGet 完成校验和索引，再从独立项目安装验证。

消费者的 PackageReference 见下方；`1.0.0` 是正式版本，不需要启用“包括预发行版”。

官方说明：[创建与发布 NuGet 包](https://learn.microsoft.com/en-us/nuget/quickstart/create-and-publish-a-package-using-the-dotnet-cli)。

## 包

当前可打包项目：

- `FlyAotOrm.Abstractions`
- `FlyAotOrm.Core`
- `FlyAotOrm.Generator`

不会打包：

- `samples/Console`
- `tests/FlyAotOrm.Generator.Tests`

## 版本

当前包版本在根目录 `Directory.Build.props` 中统一设置：

```xml
<Version>1.0.0</Version>
```

## 输出目录

默认包输出目录：

```text
artifacts/packages
```

## 打包命令

```powershell
dotnet pack FlyAotOrm.slnx -c Release --no-restore /p:UseSharedCompilation=false
```

只打单个项目：

```powershell
dotnet pack src\FlyAotOrm.Abstractions\FlyAotOrm.Abstractions.csproj -c Release --no-restore /p:UseSharedCompilation=false
dotnet pack src\FlyAotOrm.Core\FlyAotOrm.Core.csproj -c Release --no-restore /p:UseSharedCompilation=false
dotnet pack src\FlyAotOrm.Generator\FlyAotOrm.Generator.csproj -c Release --no-restore /p:UseSharedCompilation=false
```

## Generator 包形态

`FlyAotOrm.Generator` 是 analyzer/source generator 包：

- 不输出普通 `lib` 编译引用。
- 生成器 DLL 打进 `analyzers/dotnet/cs`。
- `Microsoft.CodeAnalysis.CSharp` 使用 `PrivateAssets="all"`，不作为消费者运行时依赖。

消费者项目通常需要引用：

```xml
<PackageReference Include="FlyAotOrm.Abstractions" Version="1.0.0" />
<PackageReference Include="FlyAotOrm.Core" Version="1.0.0" />
<PackageReference Include="FlyAotOrm.Generator" Version="1.0.0" PrivateAssets="all" />
```

## 发布前检查

正式发布前至少确认：

- `dotnet test` 通过。
- `dotnet build` 无 warning。
- NativeAOT sample publish 通过。
- 发布产物 sample 运行通过。
- `dotnet pack` 通过。
- 包内容包含 README。
- Generator 包包含 `analyzers/dotnet/cs/FlyAotOrm.Generator.dll`。

包消费验证方法见 [PackageSmoke](../tests/PackageSmoke/README.md)。签名、SourceLink、symbols package 和版本策略自动化可后续完善；NuGet 是否已发布以实际上传结果为准。
