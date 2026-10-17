# 原生云母与背景层验证

发布前更新：增强后的 Mica Alt 已在最终验收中通过三种主题及实际合成窗口检查，见 [FINAL-ACCEPTANCE.md](FINAL-ACCEPTANCE.md)。以下“未测试”说明仅对应当时按要求只构建的那一轮，保留以区分历史结果。

2026-10-11，Windows 11 10.0.26300.0，x64，150% DPI。

后续视觉调整（同日）：按用户要求增强效果，改为 `MicaKind.BaseAlt`，使用壁纸着色更明显的系统 Mica Alt；导航内容背景沿用 `LayerFillColorDefault` 的主题颜色，但将画刷透明度乘以 0.55，减少遮盖。高对比度继续使用不透明系统窗口颜色；不支持材质时使用对应的 `SolidBackgroundFillColorBaseAltBrush`。本次只构建发布，按用户要求未运行测试。增强版位于 `artifacts/publish/mica-alt-win-x64/ClipFlow.exe`。下方测试及截图来自此前的标准 Mica 版本，不代表增强版已通过运行验证。

主窗口显式使用 `MicaBackdrop` / `MicaKind.Base`。根布局、导航底层和标题栏保持透明；导航内容继续使用 WinUI 原生 `LayerFillColorDefaultBrush` 半透明层。不支持材质或材质创建失败时，独立的底部纯色层使用 `SolidBackgroundFillColorBaseBrush`，随主题立即更新。截图用的纯色层仅在控件渲染测试期间启用，实际窗口保持关闭。

云母采样桌面壁纸，而非后方应用的图片。窗口失去激活状态、关闭系统透明效果、节能或高对比度等情况下，原生材质可能回退为纯色。控制中心的亚克力与云母属于不同材质，不能用后方照片是否透出来判断云母是否生效。这里保留系统原生激活和回退行为，没有强行保持失焦窗口透明。

依据：[Microsoft 云母设计与回退行为](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)、[Windows App SDK 系统背景 API](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)。

## 测试结果

运行 `scripts/Test.ps1 -Integration -BuildOutputDirectory artifacts/mica-debug`：

- 27 项核心检查、16 组真实 WinUI / 剪贴板集成检查通过。
- 新进程恢复历史、收藏、主题、暂停状态与数量上限通过。
- 托盘原生菜单、隐藏后继续记录、实际鼠标暂停/继续、恢复、重复启动与退出通过。
- 云母支持状态为 True；浅色、深色、跟随系统均核对原生材质连接、根背景透明、导航面板透明、内容层透明度及截图背景关闭。
- 模拟材质不可用，核对纯色回退、浅深色切换；隐藏并恢复窗口后材质连接仍存在。
- Debug 构建 0 警告、0 错误；专项报告见 [mica-results.txt](mica-results.txt)。
- Release x64 self-contained 发布成功，版本仍为 1.0.0.0、作者 OpticsValley。发布时 NuGet 漏洞元数据服务不可达，出现 NU1900；依赖使用已有缓存，编译成功，不计为已完成联网依赖审计。

三张截图通过 GDI 读取隔离测试窗口的真实桌面合成结果，包含标题栏和 Mica，没有使用 `RenderTargetBitmap` 的纯色替代层。测试仅临时激活独立夹具窗口，不更改壁纸、透明效果、节能和辅助功能设置。

- 浅色实际窗口（本地验收截图：`screenshots/mica-native-Light.png`）
- 深色实际窗口（本地验收截图：`screenshots/mica-native-Dark.png`）
- 跟随系统实际窗口（本地验收截图：`screenshots/mica-native-System.png`）

前两次诊断运行分别因取到导航项中的同名模板元素、测试窗口未获前台焦点中断，均未计为通过；修正诊断夹具后重新运行完整流程通过。失败报告保留于各自的 `artifacts/integration-*` 目录。

高对比度、关闭系统透明效果、节能模式的实际系统开关尚未逐项切换验证；本轮纯色回退通过受控禁用材质连接验证。

新版发布位于 `artifacts/publish/win-x64/ClipFlow.exe`，需要保留整个发布目录。若旧实例仍在托盘运行，先通过托盘“退出”后再启动新版；单实例机制会将直接重复启动转发到旧进程。
