# 发布前最终验收

日期：2026-10-11。ClipFlow 1.0.0.0，作者 OpticsValley。实际运行环境：Windows 11 build 26300 x64、150% DPI、Windows App SDK 2.2.0。

## 本次交付

- 图片预览支持普通鼠标滚轮放大/缩小，以鼠标位置为中心；保留 + / −、适应窗口、100%、左键拖动和原图高清显示。原生滚轮输入、锚点、单次事件不重复、上下限、空白区域及缩放后的双轴拖动均通过。
- 最低系统配置为 Windows 10 2004 / build 19041。csproj、平台兼容分析最低版本、源清单和真实 MSIX 清单一致；编译 SDK 不变。维护者反馈旧版已在 Win10 实机启动。本轮修复 Win10 深色窗口顶部白条：采用主题一致的 WinUI 标题栏按钮，调用原生窗口操作，保留标题拖动、双击与关闭到托盘。Windows 11 保留 Mica Alt 和原系统按钮。详情见 [标题栏修复](WINDOWS10-TITLEBAR.md)。
- 本轮强制 Win10 外观完整集成通过，报告含 17 条 PASS；重启及托盘通过，核心 27 项通过，新增标题栏专项 5 项通过。Win11 的三种主题、Mica/回退、后台恢复和托盘另行通过。本轮没有重复 Win11 完整 17 组，上一轮的正常路径完整报告仍保留。
- Release 自包含发布成功，实际运行的 7 项冒烟全部通过：启动、托盘、关闭后台、单实例、恢复、退出及移除图标。品牌资源与版本/作者校验通过。
- Store 上传包成功生成，包含 x64 MSIX 与 appxsym。实际包内 Partner Center 身份、1.0.0.0、最低系统 19041、图标、运行时、许可证、入口和唯一 runFullTrust 权限通过；MakeAppx 验证解包通过。约 99.2 MiB。
- 工作区当前源清单保留本地开发签名 CN=OpticsValley。商店脚本独立生成 Publisher 为 CN=3F646DDF-EFA2-4801-82BE-82D7E5808841 的清单并核验真实包，不覆盖调试签名。名称尾部横线仍保留。
- 全局路径扫描通过；新增 ARM64 发布配置的本地绝对目录已替换为相对目录。源码网络客户端扫描、许可证、隐私政策、贡献指南、忽略规则等发布检查通过。
- MIT / OpticsValley、README 徽章、PRIVACY、CONTRIBUTING 及第三方许可均保留。测试数据仍隔离，Release 不含测试开关或诊断代码。

## 产物

- 最新商店包：artifacts/release/ClipFlow_1.0.0.0_x64.msixupload。
- 最新源码：artifacts/release/ClipFlow-1.0.0.0-source.zip。
- 便携版：artifacts/publish/titlebar-win-x64/，需要分发整个目录；compat-win-x64 是上一轮旧版。
- 校验和：artifacts/release/SHA256SUMS.txt。

商店包 SHA-256：C5EE7D8360AC11201EB3B0AC44201C35CF90707F39A85905C48399EE78AA263B。

本次原始商店包：artifacts/store-2e3fdc1da5e94f28a03e23ccbc7726f9/packages/ClipFlow_1.0.0.0_x64.msixupload。

Partner Center 实际上传反馈表明，上一包的显示名称 ClipFlow 未被保留。本轮改为维护者截图中的保留名称 `ClipFlow - 剪贴板工具`，同步 Package/Properties 和应用 VisualElements。名称、Publisher 等商店身份集中到 scripts/StoreIdentity.json；新包检查通过，旧包在负向验证中正确被拒绝。版本仍为 1.0.0.0。本地包验证不能替代重新上传后的 Partner Center 结果。

报告：[集成](integration-results.txt)、[重启](restart-results.txt)、[托盘](tray-results.txt)、[云母/回退](mica-results.txt)、[降级集成](legacy-integration-results.txt)、[降级重启](legacy-restart-results.txt)、[降级托盘](legacy-tray-results.txt)、[降级背景](legacy-mica-results.txt)、[Release 冒烟](release-smoke-results.txt)、[图标](brand-results.txt)、[包内检查](store-package-results.txt)。

此前发布准备中已从干净源码在中文/空格路径构建通过，核心测试从其他工作目录通过。此轮没有再次执行干净机器安装/升级或远程 GitHub CI，不能算作本次重复验收。

## 验收边界

本机没有 Windows 10 实机或虚拟机。维护者提供的实机运行截图属于上一版，当前标题栏修复仍需在那里复核。旧系统外观路径在 Windows 11 上通过，并不等同于 Windows 10 的实际系统 DLL、驱动、签名包安装及升级验收。仍应在 Windows 10 22H2 x64 验证新版运行和安装/升级/卸载。

尚未执行 WACK，尚未实际上传 Partner Center 或推送 GitHub。结构正确的上传包不等于商店认证通过。需要公开隐私政策 HTTPS URL、支持信息和商店展示资料；步骤见 [Store 发布指南](STORE-RELEASE.md)。部分本地构建存在 NU1900 网络警告，在线依赖漏洞审计尚未完成。

本轮早期回归出现剪贴板超时和焦点/桌面遮挡问题。正常退出旧版并行实例、维护者解锁后，最终 Win10 分支完整集成、重启、托盘和新 Release 冒烟成功退出；失败记录没有计入通过数，没有删除断言或关闭输入保护。
