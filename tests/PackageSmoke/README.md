# Package consumer smoke test

This project references only NuGet packages, not the FlyAotOrm source projects.
It reuses the console sample to check generated entity metadata, CRUD,
repositories, projections, and SQLite execution. A project-local package cache
avoids accidentally testing another build from the user-wide cache.

First pack the solution from the repository root:

```powershell
dotnet pack FlyAotOrm.slnx -c Release
```

Then run these commands from this directory. The SDK selection intentionally
uses .NET 8 to verify the supported compiler baseline:

```powershell
dotnet --version
dotnet run -c Release
dotnet publish -c Release -f net8.0 -r win-x64
./bin/Release/net8.0/win-x64/publish/PackageSmoke.exe
# 同样可对 net10.0 / net11.0 重复（替换 -f 与路径中的 TFM）
```

Keep this project outside the main solution: its package dependencies require
the solution to have been packed first. Publish restores the NativeAOT tools;
an appropriate native toolchain must be installed on the build machine.
