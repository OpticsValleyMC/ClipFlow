# 核心测试

这是可执行验收项目，名称是 `ClipFlow.Tests`，使用 `dotnet run`，不使用 `dotnet test` 适配器。

要求 Windows 10 2004 或更新版本与 .NET SDK 8 或更新版本，存储测试调用 Windows 自带的 `winsqlite3.dll`。不支持 Linux/macOS。首次还原需要 NuGet 网络访问。

在仓库根目录执行 `./scripts/Test.ps1`，或直接执行：

```powershell
dotnet run --project ./ClipFlow/ClipFlow.Tests/ClipFlow.Tests.csproj
```

脚本使用 `$PSScriptRoot` 定位仓库，可以从其他工作目录调用。程序使用 `AppContext.BaseDirectory/artifacts/tests/<GUID>/` 隔离每次测试，`Path.Combine` 生成所有路径，不依赖开发者用户名、盘符或当前工作目录；包含带中文和空格的目录测试，结束后仅删除本次 GUID 数据目录，不接触正常应用历史。

`./scripts/Test.ps1 -Integration` 另外构建并运行 Debug WinUI 应用，需要交互式桌面，会用非敏感样例替换 Windows 系统剪贴板。集成数据保存到仓库 `artifacts/integration-*`，不应提交到 Git。
