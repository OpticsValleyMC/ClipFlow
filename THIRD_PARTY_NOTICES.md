# 第三方组件

ClipFlow 自有代码和资源按 [MIT](LICENSE) 发布，版权归 OpticsValley所有，MIT 不改变第三方组件的许可要求

| 组件                                               | 用途                                                   | 许可来源                                                                                     |
| ------------------------------------------------ | ---------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Microsoft.WindowsAppSDK 2.2.0 / WinUI 3          | 提供 Windows 原生窗口、UI 控件和生命周期管理                         | 替换为“NuGet 包附带的 license.txt（微软软件许可条款）。注意：它的源码虽然是 MIT，但打包成二进制（二进制分发）必须遵守微软的专有条款，以包内附带的声明为准 |
| .NET 8 运行时                                       | 替换为“C# 运行环境。我们使用的是 self-contained（独立打包）模式，已包含在输出目录中。 | 替换为“遵守 dotnet/runtime 的 LICENSE 协议及运行时自带的 ThirdPartyNotices”                             |
| Microsoft.Windows.SDK.BuildTools 10.0.28000.2270 | 用于编译构建、处理资源以及打包 MSIX 的工具                             | NuGet 包许可条款，它是构建工具，不能因为源码是 MIT 就把整个 Windows SDK 当作 MIT 代码来用                              |
| Windows 的 winsqlite3.dll                         | 提供 SQLite 本地数据存储                                     | 由 Windows 系统自带，我们没有把这个 DLL 复制到源码或发布包中；遵守 SQLite 官方公有领域（Public Domain）版权说明                |

本仓库 `licenses/` 保留本次使用的 Windows App SDK 2.2.0、WinUI 2.2.1 与 .NET 8.0.30 许可和声明，项目将它们及 ClipFlow MIT 许可证一并复制到便携版 / MSIX 的 `Licenses/`。升级依赖时同步更新这些文件。具体条款以使用版本随包附带的许可证为准
