# ClipFlow 验收记录

2026-10-11 发布准备验收已覆盖增强后的 Mica Alt：27 项核心、16 组 UI 集成、重启、托盘独立复测及 Release 7 项冒烟通过。最新报告、失败记录和 Store 认证边界见 [FINAL-ACCEPTANCE.md](FINAL-ACCEPTANCE.md)。下面保留历史各轮说明。

日期：2026-10-10。环境：Windows 11 **10.0.26300.0**、x64、150% 显示缩放、.NET SDK **10.0.400**、Windows App SDK **2.2.0**、系统 SQLite **3.51.1**。

2026-10-11 后续视觉调整：按用户要求改为 Mica Alt，并将内容背景画刷透明度乘以 0.55。此次只构建发布，没有运行测试；以下回归结果对应增强前的标准 Mica 版本。增强版路径为 `artifacts/publish/mica-alt-win-x64/ClipFlow.exe`。

2026-10-11 更新：云母底层显式透明，材质不支持时使用独立主题回退层；最新完整回归为 **27 项核心、16 组集成、新进程重启及托盘专项通过**，含三种主题下真实桌面合成截图。见 [MICA-FIX.md](MICA-FIX.md)。图片受管副本与托盘暂停/继续记录的实现见 [IMAGE-FILE-AND-TRAY-RECORDING.md](IMAGE-FILE-AND-TRAY-RECORDING.md)。以下历史验收结果保留；集成/重启/托盘报告已更新为本轮结果。

最新托盘右键菜单闪退修复及专项回归范围见 [TRAY-MENU-FIX.md](TRAY-MENU-FIX.md)，包括菜单持续显示、重复事件、取消后重开和实际鼠标选择“打开”。

当前版本 **1.0.0.0**，作者 **OpticsValley**。本轮图标、exe 元数据与拖动验收详见 [ICON-AND-PAN.md](ICON-AND-PAN.md) 和 [brand-results.txt](brand-results.txt)。实际鼠标拖动的四次样本均核对了双轴偏移及释放；交互夹具结束超时与用户 Esc 停止桌面自动化的情况按原样保留，不计为完整交互夹具通过。

## 自动验证结果

- Debug x64 构建成功，0 个编译错误。
- Release x64 self-contained 目录发布成功，运行文件位于 `artifacts/publish/win-x64/ClipFlow.exe`，需保留其整个目录。
- 最新托盘修复后的 Release 实际进程冒烟：窗口启动、Shell 托盘注册、隐藏、重复启动恢复、退出码 **0** 及图标移除通过，报告见 [release-smoke-results.txt](release-smoke-results.txt)。菜单专项另覆盖持续显示、重入保护及实际鼠标点击“打开”；之前图标与拖动轮次被用户停止的桌面自动化按原验收记录保留。
- **27 项核心检查通过**，包括 200 万字符文本重启后的精确内容及 Unicode / 换行分页边界。
- **16 组真实 WinUI / Windows 剪贴板集成检查通过**；**1 组进程重启恢复检查通过**；另验证系统托盘注册、原生菜单、隐藏后的真实剪贴板记录、恢复、重复启动、任务栏重建与退出。

运行入口：`scripts/Test.ps1 -Integration`。完整报告见 [integration-results.txt](integration-results.txt)、[restart-results.txt](restart-results.txt) 与 [tray-results.txt](tray-results.txt)。数据全部来自隔离测试目录内的非敏感 fixture，未读取或清空用户原有 ClipFlow 历史。

| 要求 | 验证方式与结果 |
| --- | --- |
| 原生主窗口 | 实际启动 WinUI exe，加载 NavigationView、ListView、原生设置控件；采集浅色、深色、跟随系统的 XAML 渲染 |
| 云母背景 | 核对原生 MicaBackdrop、透明根布局/导航及半透明内容层；三种主题采集真实桌面合成截图；模拟不支持材质的主题回退；后台恢复后材质连接保留 |
| 文本与图片监听 | 通过真实 Windows Clipboard API 写入文本与 320×180 位图，等待监听自动落库和更新列表 |
| 搜索、收藏、复制、删除 | 执行视图模型的真实命令和筛选；核对收藏页、空搜索结果、匹配结果及内部复制后的行数 |
| 重启持久化 | 完全结束第一个应用进程，再启动新 exe；从 SQLite 恢复收藏文本、深色主题、暂停状态和上限 50 |
| 三种主题、窄布局 | 实际切换 RequestedTheme；在 1100×760 与 760×560 DIP 窗口渲染；检查导航收起、控件布局和页面滚动 |
| 重复内容 | 连续复制相同文本、相同图片；核对只有一条对应记录，ID / 收藏保留，时间更新并置顶 |
| 剪贴板占用与恢复 | 独立进程写入并短暂锁定剪贴板，有限重试后成功；持续占用时跳过，后续复制恢复正常 |
| 图片管理 | 原图与缩略图写入独立文件；实际加载 BitmapImage 并核对原始宽高比；缺失和损坏文件触发占位；删除后两份文件消失 |
| 高清原图 | 真实剪贴板写入 1920×1080 位图；生成 1024×576 缩略图；打开真实图片对话框，核对原始像素、100% 缩放和滚动；损坏原图显示占位 |
| 图片比例与缩放 | 1632×1131、240×1280、1280×240、20×4000 图片分别在标准与最小窗口打开；检查完整适应、居中、横向空白收紧；通过原生按钮自动化实际触发 + / − 并核对缩放倍率 |
| 鼠标平移 | 使用 computer-use 的真实鼠标输入，200% 下四次拖动得到 280/240、120/120、0/0、170/150 偏移，均释放指针；包括反向、拖出预览区及再次拖动，原始样本保留 |
| 版本、作者与正式图标 | exe 文件/产品版本 1.0.0.0，公司 OpticsValley；项目 Authors 与清单一致；高分辨率透明主图、49 个包 PNG、45 个任务栏主题尺寸变体和 15 尺寸 ICO 解码检查通过 |
| 系统托盘与后台运行 | Shell_NotifyIconGetRect 核对图标真实注册；读取原生菜单文字；发送系统关闭消息后确认窗口隐藏，真实复制内容仍入库；隐藏时触发托盘上下文菜单，GetGUIThreadInfo 核对真实弹出状态；托盘选择恢复；第二进程退出并恢复第一实例；模拟任务栏重建后恢复图标；发送原生菜单退出命令，进程退出码 0 |
| 长文本详情 | 真实剪贴板写入 324,012 字符文本；打开详情、滚动、翻到末页并验证结尾；再次复制核对完整原文；重开数据库核对内容；最小窗口下对话框不溢出；超过 200 万字符的输入跳过 |
| 空状态说明 | 1100×760 DIP 下实际渲染，说明完整显示在一行且未省略；窄窗口保持单行，空间不足时省略并提供完整悬浮提示 |
| 暂停与隐私 | 暂停期间复制不入库；带 `IsAllowedInHistory=false` 的真实系统剪贴板内容不入库 |
| 数据清空 | 取消确认保留数据；确认清空全部删除记录、图片并重置设置；存储层检查历史清空保留设置 |
| 数据迁移 | 创建真实 v1 SQLite 文件，迁移到 v2，保留内容、收藏和设置；较新 Schema 拒绝打开且数据不被删除 |
| 初始化失败恢复 | 损坏数据库初始化失败时原文件保留；用户明确清空全部的数据恢复方法可重新初始化为空库 |

核心检查另覆盖 Unicode、SQL 参数化文本、嵌入 NUL、数量上限、图片哈希包含尺寸、永久失败重试上限、非临时异常不重试和取消。

## 界面渲染

本轮云母专项另采集包含系统合成材质的真实窗口截图：浅色（本地验收截图：`screenshots/mica-native-Light.png`）、深色（本地验收截图：`screenshots/mica-native-Dark.png`）、跟随系统（本地验收截图：`screenshots/mica-native-System.png`）。报告见 [mica-results.txt](mica-results.txt)。

以下图片由实际运行中的 WinUI 控件通过 `RenderTargetBitmap` 生成。该 API 不包含系统标题栏按钮及 compositor 的 Mica 材质，因此截图临时使用应用本身的主题纯色回退背景；实际运行窗口启用 Mica 和系统标题栏按钮。

- 浅色最近记录（本地验收截图：`screenshots/recent-Light.png`）
- 深色最近记录（本地验收截图：`screenshots/recent-Dark.png`）
- 设置页（本地验收截图：`screenshots/settings.png`）
- 窄窗口记录页（本地验收截图：`screenshots/recent-narrow.png`）
- 窄窗口设置页（本地验收截图：`screenshots/settings-narrow.png`）
- 新进程恢复收藏与暂停状态（本地验收截图：`screenshots/restart.png`）
- 长文本详情（本地验收截图：`screenshots/long-text-detail.png`）
- 窄窗口长文本详情（本地验收截图：`screenshots/long-text-narrow.png`）
- 原图适应窗口（本地验收截图：`screenshots/image-detail-fit.png`）
- 原图 100%（本地验收截图：`screenshots/image-detail-100.png`）
- 1632×1131 比例预览（本地验收截图：`screenshots/image-aspect-1632.png`）
- 窄窗口比例预览（本地验收截图：`screenshots/image-aspect-1632-narrow.png`）
- 居中竖图（本地验收截图：`screenshots/image-aspect-240.png`）
- 单行空状态（本地验收截图：`screenshots/recent-empty.png`）

## 验证范围

自动集成检查调用真实视图模型命令、系统剪贴板和 WinUI 渲染；详情测试实际打开 ContentDialog，并操作滚动、分页与缩放，并非逐一模拟鼠标点击。对话框截图只包含弹出内容，外围透明区域在部分预览中呈黑色。系统窗口按钮、拖动与贴靠保留原生行为；键盘焦点与可访问名称使用原生控件。尚未逐项人工验证屏幕阅读器、多显示器 DPI 切换、高对比度、系统动画关闭，以及 Windows 11 最低 build / x86 / ARM64 设备；应在发布到更广用户范围前补充这些环境检查。

联网受限时构建出现 `NU1900`，表示 NuGet 漏洞元数据检查无法连接；依赖仍从已有缓存还原并成功编译，不代表已完成依赖安全审计。
