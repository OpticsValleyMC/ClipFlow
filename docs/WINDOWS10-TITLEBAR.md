# Windows 10 标题栏修复

2026-10-11，ClipFlow 1.0.0.0，OpticsValley。

Windows 10 实机截图中，应用内容使用深色主题，系统标题栏却保留白色。Windows 10 忽略 AppWindowTitleBar 的按钮颜色设置，因此仅改这些颜色不能修复白条。

现在 Windows 10 使用与浅色、深色、跟随系统主题一致的 WinUI 标题区域，显示原应用图标及名称。收起原系统标题栏按钮，提供主题资源驱动的 WinUI 按钮；最小化、最大化/还原调用 OverlappedPresenter，关闭发送 WM_CLOSE，沿用应用现有关闭到托盘和退出策略。SetTitleBar 的拖动区与右侧按钮分开，标题双击交给原生窗口。按钮支持键盘和可访问名称，最大化后更新还原图标及名称。未使用未文档化的 Windows 10 DWM 深色属性。

Windows 11 继续使用原有透明系统标题栏按钮和 Mica Alt。本次不改变 Win11 材质强度、字体或导航设计。

官方依据：[自定义标题栏与 Windows 10 限制](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar)、[TitleBarHeightOption](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.titlebarheightoption)。当前会话无 Context7 工具，已查阅微软官方资料并核对安装的 Windows App SDK 2.2 API。

## 本轮验证

运行环境为 Windows 11 build 26300 x64、150% DPI，分别运行真实 Win11 外观和 Debug 强制 Windows 10 外观分支。

- 核心 27 项通过。
- Windows 10 分支：标题栏 5 项通过，验证原生拖动命中、最大化/还原、最小化、关闭到托盘、标题双击；浅色/深色/系统主题、纯色回退、后台恢复通过。已查看实际桌面合成的浅/深色截图。
- Windows 10 分支完整集成 17 组通过，含文本/图片、滚轮和拖动、剪贴板占用恢复、持久化、删除及清空；新进程重启和托盘测试通过。
- Windows 11 分支：Mica Alt、三种主题、纯色回退、后台恢复及托盘测试通过。
- 新 Release 的 7 项实际进程冒烟通过。

早期完整回归曾出现剪贴板超时、输入失去焦点；随后桌面锁定时输入保护停止截图。正常退出旧版并行实例、维护者解锁后重新执行流程，最终完整集成成功。未关闭输入保护，未删除功能断言，失败运行没有计入通过数。

报告：[标题栏](titlebar-results.txt)、[完整降级集成](legacy-integration-results.txt)、[降级重启](legacy-restart-results.txt)、[降级托盘](legacy-tray-results.txt)、[降级背景](legacy-mica-results.txt)、[Win11 背景](mica-results.txt)、[Win11 托盘](tray-results.txt)、[Release 冒烟](release-smoke-results.txt)。

复现：

```powershell
./scripts/Test-TitleBar.ps1 -LegacyChrome
./scripts/Test-TitleBar.ps1
./scripts/Test.ps1 -Integration -LegacyChrome -BuildOutputDirectory artifacts/titlebar-debug
```

测试期间须保持桌面解锁，不操作其他窗口。测试数据在隔离目录；诊断开关不编译进 Release。维护者旧版 Win10 实机运行反馈不能代替本轮标题栏修复的实机复核；当前未执行 Win10 新版实机验收或 WACK。
