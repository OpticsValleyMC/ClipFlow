# Windows 10 兼容与图片滚轮验收

日期：2026-10-11。版本 1.0.0.0，作者 OpticsValley。

## 使用变化

图片详情中，普通鼠标滚轮向上放大、向下缩小，无需按 Ctrl。一格缩放 1.25 倍，以鼠标所在位置为中心，+ / − 按钮以预览中心缩放；保持原图比例与原始像素。滚轮也可用于图片四周的空白区域。缩放限制沿用 ScrollViewer 的 10%–800%，极端宽高比图片按适应窗口的基础比例计算。放大后继续支持左键拖动、滚动条、触摸和键盘。

监听图片和 ScrollViewer 的滚轮事件，并去除同一事件的重复处理。每次调用非动画 ChangeView；不创建后台轮询、额外图像解码或无限缩放任务。未加载和加载失败时不缩放，关闭详情时释放图像和指针状态。

## 系统兼容

| 项目 | Windows 10 | Windows 11 |
| --- | --- | --- |
| 最低配置 | 2004 / build 19041，x64；推荐 22H2 | build 22000 或更新，x64 |
| 窗口背景 | 浅/深色主题纯色回退 | Mica Alt；不支持材质时同样回退 |
| 标题栏 | 与主题一致的 WinUI 标题区域及按钮，调用原生窗口操作 | 自定义标题区域、系统按钮与贴靠 |
| 字体 | Segoe UI、Segoe MDL2 Assets | WinUI 系统默认字体与图标资源 |
| 剪贴板、SQLite、托盘 | 同一套本地功能 | 同一套本地功能 |

最低配置在 csproj 的 TargetPlatformMinVersion / SupportedOSPlatformVersion 和 MSIX TargetDeviceFamily 中同步为 10.0.19041.0。编译目标仍为 net8.0-windows10.0.22621.0，依赖仍为 Windows App SDK 2.2.0，无须为兼容旧系统降级 SDK。标题栏定制在支持的 Windows 10 / 11 系统启用，云母先查询 MicaController.IsSupported。原生 DPI、SQLite、托盘及剪贴板 API 的声明已检查。

Windows 10 忽略 AppWindow 原生标题栏按钮颜色设置。本轮收起系统白色标题栏，使用 WinUI 主题按钮配合原生窗口操作，保留系统标题拖动和双击；Windows 11 的云母和系统按钮保持原有行为。新增验收见 [标题栏修复](WINDOWS10-TITLEBAR.md)。

官方依据：[SDK 版本与最低系统](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)、[系统背景材质](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)、[标题栏及 Windows 10 限制](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar)、[滚轮路由事件](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerwheelchanged?view=windows-app-sdk-1.8)。当前会话没有 Context7 工具，使用官方资料和实际安装 SDK 核对。

## 已执行验收

运行环境：Windows 11 build 26300、x64、150% DPI。普通路径和强制旧系统外观路径各完整执行一次最终通过流程。

- 核心 27 项通过：长文本、Unicode、文本/图片去重、受管图片、收藏、持久化、数量上限、Schema 迁移/失败保护、有限重试与取消。
- 两套真实桌面集成均通过：既有 16 组功能和新增鼠标输入专项（报告共 17 条 PASS）。原生滚轮放大/缩小、单次事件不重复、鼠标锚点、上下限、图片空白区域、缩放后的双轴拖动和释放均通过。
- 两套新进程重启均通过：历史、收藏、主题、暂停及数量上限恢复。
- 两套原生托盘均通过：右键菜单、暂停/继续、隐藏后台采集、恢复窗口、重复启动、任务栏重建和退出。
- 浅色、深色、跟随系统、760×560 窄窗口与对话框布局通过；纯色回退不重复显示自定义标题栏、不留下标题空行。已查看实际窗口与图片详情截图。
- Release 商店包构建成功；真实包内最低系统 19041、Partner Center Publisher、版本、资源、运行时及权限均通过，MakeAppx 正常验证解包。

首轮鼠标拖动测试使用 SetCursorPos，拖动移动事件未送达 WinUI；改为 SendInput 相对移动后通过。另一次测试因前台焦点被打断停止输入；现在在输入前激活测试窗口，并保留窗口/进程/遮挡检查。这些失败没有计为通过，也没有删除断言。

正常报告：[集成](integration-results.txt)、[重启](restart-results.txt)、[托盘](tray-results.txt)、[背景](mica-results.txt)。降级报告：[集成](legacy-integration-results.txt)、[重启](legacy-restart-results.txt)、[托盘](legacy-tray-results.txt)、[背景](legacy-mica-results.txt)。包检查：[store-package-results.txt](store-package-results.txt)。

复现：

```powershell
./scripts/Test.ps1 -Integration -BuildOutputDirectory artifacts/compat-debug
./scripts/Test.ps1 -Integration -LegacyChrome -BuildOutputDirectory artifacts/compat-debug
./scripts/Build-Store.ps1
```

集成输入仅作用于本次隔离测试进程，暂时移动鼠标，并替换剪贴板为测试内容；测试期间不要操作其他窗口。Release 不包含测试开关或诊断代码。

维护者已反馈上一版在 Windows 10 实机运行并提供截图；本轮更新标题栏仍需在那里复核。本机没有 Windows 10 实机或虚拟机；强制旧系统外观测试仅证明降级分支在当前系统下通过，不证明 Windows 10 的实际 DLL、驱动、安装或升级兼容性。签名包首次安装、升级和卸载，以及 WACK / Partner Center 认证仍需在对应环境执行。
