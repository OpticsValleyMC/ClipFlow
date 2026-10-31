# ClipFlow 1.1.0.0 最终验收

日期：2026-10-31。作者：OpticsValley。当前验收环境为 Windows 11 x64；最低系统为 Windows 10 2004 / build 19041。

## 本轮功能

- 新增默认 Alt+V 全局快捷键和快捷粘贴浮窗。支持自定义快捷键、保存及冲突处理，保留标准 Ctrl+V 粘贴操作。
- 浮窗支持原生标题区域拖动、独立搜索、收藏筛选、逐项收藏与删除、键盘选择及 Esc 关闭。点击或按 Enter 后恢复原输入焦点并自动粘贴；受系统限制时提供反馈。
- 调整浮窗标签、关闭按钮、卡片悬浮效果、滚动条位置及滚轮响应；保留收藏与更多按钮的悬浮反馈。
- 复制成功后按钮短暂显示绿色勾选；失败时不显示成功。搜索关键词高亮，长文本预览可显示靠后的匹配内容。
- 托盘采用标准 WinUI MenuFlyout 控件；修复被托盘溢出面板遮挡、反复打开及开机启动勾选位置问题。暂停图标和设置文案同步优化。
- 安装版使用 Windows StartupTask，默认关闭。设置页与托盘同步系统状态，后台启动先初始化存储、采集和托盘，主动打开后才显示主窗口。
- 收藏支持置顶和排序，数据库 v3 迁移保留旧数据。背景支持纯色与三档云母效果，并持久保存设置。
- 版本统一为 1.1.0.0，更新使用、隐私及 Store 发布文档和验收脚本。

## 本次自动验收结果

| 验收项目 | 结果 |
| --- | --- |
| 核心验收 | 35 项通过，涵盖数据库迁移、去重、收藏排序、图片、设置与异常恢复 |
| 常规桌面集成 | 通过，含文本、长文本、图片、鼠标缩放与平移、主题、背景、收藏及清空 |
| 兼容外观集成 | 通过，含重启、存储恢复与托盘 |
| 快捷功能与浮窗 | 25 项通过，含真实快捷键、拖动、焦点恢复、鼠标/Enter 粘贴、收藏删除及滚轮响应 |
| 标题栏与材质 | 常规及兼容模式均通过 |
| 托盘 | 多轮通过，含勾选对齐、顶层遮挡、真实鼠标操作、取消重开、单实例与任务栏重建 |
| 独立 MSIX 自启动 | 实际 StartupTask 注册、默认关闭、设置启停与外部状态刷新均通过 |
| 后台启动 | 隐藏初始化、隐藏采集、首次打开恢复均通过；临时验收包已移除 |
| Release | 构建零警告、零错误；7 项真实进程冒烟通过 |
| 品牌与发布源检查 | 版本、图标、清单、权限、资源及源代码检查通过 |
| Store 上传包 | Release x64 包和符号文件已生成，身份、内容与 MakeAppx 验证通过 |
| Git | diff 空白检查通过；未暂存、未提交 |

运行命令：

```powershell
./scripts/Test.ps1 -Integration -BuildOutputDirectory artifacts/full-debug
./scripts/Test.ps1 -Integration -LegacyChrome -BuildOutputDirectory artifacts/full-debug
./scripts/Test-QuickFeatures.ps1 -SkipBuild -BuildOutputDirectory artifacts/full-debug
./scripts/Test-TitleBar.ps1 -BuildOutputDirectory artifacts/full-debug
./scripts/Test-TitleBar.ps1 -LegacyChrome -BuildOutputDirectory artifacts/full-debug
./scripts/Test-PackagedStartup.ps1
./scripts/Test-ReleaseReadiness.ps1
./scripts/Build.ps1 -Configuration Release -OutputDirectory artifacts/full-release
./scripts/Smoke-Release.ps1 -Executable artifacts/full-release/ClipFlow.exe
./scripts/Test-BrandAssets.ps1 -Executable artifacts/full-release/ClipFlow.exe
./scripts/Build-Store.ps1
```

集成脚本内部还运行重启及托盘验收；Store 构建脚本内部运行上传包验证。首次品牌检查指向旧的默认发布目录，因旧二进制版本不符而失败；随后已针对本次新构建的 Release 重新检查并通过。

## 本次交付

- Store 上传文件：`artifacts/release-1.1.0.0/ClipFlow_1.1.0.0_x64.msixupload`。
- SHA-256：`55BA679E70D3C48E1EA7868B89234F4EF2C28E23C5683FF2F8B0703D635CAD24`。
- 测试与原始报告位置：`artifacts/release-1.1.0.0/VALIDATION.md`。
- Git 提交草稿：`artifacts/release-1.1.0.0/GIT-COMMIT-MESSAGE.txt`。

产物目录已由 .gitignore 排除；没有上传商店、暂存或提交 Git。

## 验收边界

兼容外观在当前 Windows 11 桌面模拟，不替代 Windows 10 实体机验证。自启动验收验证实际任务和启动分支，没有注销或重启用户电脑。未执行 WACK、干净机器上的签名安装升级/卸载全流程或 Partner Center 上传；本地包结构验证不等于商店认证。
