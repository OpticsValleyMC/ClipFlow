# Microsoft Store 与 GitHub 发布

维护者：OpticsValley。应用版本：1.1.0.0。当前正式打包目标：Windows 10 / 11 x64，最低 10.0.19041.0。

## 身份与清单

以下是商店包身份。源 `Package.appxmanifest` 保留本地调试签名的 `CN=OpticsValley`；`Build-Store.ps1` 在独立产物目录生成商店清单，通过 `StoreManifestPath` 传入构建。既不覆盖本地源清单，也不改变本地证书。包验证必须匹配下表 Publisher，不能把开发清单直接上传：

| 字段 | 值 |
| --- | --- |
| Package/Identity/Name | `OpticsValley.ClipFlow-`（末尾横线属于身份，不可删除） |
| Package/Identity/Publisher | `CN=3F646DDF-EFA2-4801-82BE-82D7E5808841` |
| Package/Properties/PublisherDisplayName | `OpticsValley` |
| DisplayName | `ClipFlow - 剪贴板工具`（与 Partner Center 保留名称一致） |
| Version | `1.1.0.0` |
| TargetDeviceFamily | `Windows.Desktop` |
| MinVersion | `10.0.19041.0` |
| MaxVersionTested | `10.0.22621.0`（与目标框架一致，打包工具生成值） |
| 语言 | `zh-CN` |

本次运行验收机器为 Windows 11 build 26300；最低系统和 MaxVersionTested 不是该机器版本的自动映射。Logo 指向 Assets 的逻辑资源名，实际带 scale / targetsize 的 PNG 由资源系统解析。不要将图标资源加入测试图片忽略规则。没有手机/AI 清单声明。

商店身份集中配置在 `scripts/StoreIdentity.json`。打包脚本将商店保留名称写入独立商店清单的 `Package/Properties/DisplayName` 和 `uap:VisualElements/@DisplayName`，两者均为 `ClipFlow - 剪贴板工具`。本地开发清单和应用窗口名称仍为 ClipFlow。商店产品保留名称与包 Identity Name 是两个不同字段，不能相互替代。[微软说明](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)

如果 Partner Center 报“清单使用了你未保留的显示名称：ClipFlow”，请删除上传页面里的失败文件，用当前脚本重新生成的 `.msixupload` 上传；Retry 不会修改旧包。包被拒绝时出现的“至少上传一个程序包”“设备系列不支持”等提示可能随有效包上传消失。当前包仅支持 Desktop，页面不要勾选 Mobile 或 Xbox。实际上传结果仍以 Partner Center 验证为准。

## 权限与审核说明

唯一受限功能声明为 `runFullTrust`：WinUI 3 桌面程序使用 Windows 剪贴板 API 监听复制事件、读写当前用户本地历史，并通过系统托盘提供后台记录与退出。这是普通用户完整信任桌面进程，不是管理员权限；exe 声明 `asInvoker`、`uiAccess=false`。1.1 新增 `windows.startupTask` 桌面扩展，默认 Enabled=false，用户在设置/托盘主动启用后才在登录时运行，任务管理器可控制；不写注册表 Run 键。关闭窗口时只是隐藏现有进程，没有 UWP 后台任务、Windows 服务或扩展后台执行申请。不增加 `extendedBackgroundTaskTime`、`broadFileSystemAccess`、`allowElevation` 或网络能力。

可填写到 Partner Center 的权限审核说明：

> ClipFlow 是 Windows 桌面剪贴板管理器，runFullTrust 用于普通用户权限下的剪贴板访问、本地保存及托盘交互。用户可主动启用 Windows StartupTask 开机启动，也可随时暂停、禁用或退出。应用不请求管理员权限，不安装服务或驱动，不上传数据，无云同步或遥测。

English: ClipFlow uses runFullTrust for standard-user desktop clipboard access, local storage and tray controls. Users may opt into Windows StartupTask sign-in launch and can pause, disable startup or exit. No administrator privileges, services, drivers, data uploads, cloud sync or telemetry are used.

## 构建上传包

在安装 WinUI / 单项目 MSIX 工具的 Visual Studio 环境中运行：

```powershell
./scripts/Test-ReleaseReadiness.ps1
./scripts/Test.ps1 -Integration -BuildOutputDirectory artifacts/final-debug
./scripts/Test.ps1 -Integration -LegacyChrome -BuildOutputDirectory artifacts/final-debug
./scripts/Build-Store.ps1
# 可单独复核已生成的包：
./scripts/Test-StorePackage.ps1 -UploadPath '<实际生成的 msixupload 路径>'
```

脚本自动定位 VS MSBuild.exe（也可传 `-MSBuildPath`），以 Release、自包含 .NET/Windows App SDK、x64、`StoreUpload`、`AppxBundle=Never`、不本地签名生成 `.msixupload`。上传 ZIP 中包含 `.msix` 和符号包；不能将便携目录或单个 exe 重命名成上传包。关闭本地签名符合 Store 构建流程，商店负责正式签名；侧载另需匹配 Publisher 且受信任的证书，不提交私钥。

每次构建使用新的 `artifacts/store-*` 目录，避免误用旧包。脚本会核对真实包的身份、版本、入口、运行时、图标、权限及 MakeAppx 解包验证，并输出 SHA-256 与验收报告。结构检查不等于商店认证通过。

## 提交前需在账号中完成

1. 确认 Partner Center 当前产品身份与上述值完全一致，应用名称已保留，开发者账号验证完成。
2. 将根目录 [PRIVACY.md](../PRIVACY.md) 托管为公开可访问页面，把最终 HTTPS 地址填入商店隐私政策 URL。GitHub 仓库公开后可使用该文档的公开地址，或启用 GitHub Pages。不要填写本地路径、私有仓库地址或未建立的占位 URL。
3. 填写中文应用描述、截图、分类、年龄分级、支持联系方式与权限说明。说明后台运行方式、暂停入口、开机启动默认关闭且可由 Windows 管理，以及不支持 OCR；不要宣称自动识别所有敏感信息。
4. 在 Windows App Certification Kit (WACK) 对可安装的签名包执行认证检查，并在干净 Windows 10 / 11 x64 环境验证安装、首次启动、图片和文本采集、退出、卸载、更新后数据保留。开发机源码验收不能代替这些结果。
5. 上传本次 `.msixupload`，检查 Partner Center 的包分析结果。实际账号校验、受限能力审核和 Microsoft Store 最终认证只能由该环境确认，不能预先保证审核成功。首次版本为 1.0.0.0；后续更新需递增版本，Store 版本最后一段保持 0。

参考：[MSIX 上传包与签名](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/ci-for-winui3)、[应用权限](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)、[包要求](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/app-package-requirements)、[产品身份](https://learn.microsoft.com/en-us/windows/apps/publish/product-management-and-services)。

## GitHub 发布

保留 README、LICENSE、PRIVACY、CONTRIBUTING、THIRD_PARTY_NOTICES、源码、脚本、品牌资源和必要文档。不要提交 artifacts、bin、obj、个人 IDE 配置、AppPackages、数据库、测试图片、证书或账号关联文件；已被 Git 跟踪的文件不会因添加 .gitignore 自动移除，应在首次提交前检查暂存文件列表。

仓库尚未初始化时可在检查后执行 `git init`，建立自己的远程，再提交和推送。本轮准备不代替公开发布授权，不自动创建远程仓库或上传剪贴板测试截图。公开资源中的测试报告应使用相对路径；真实截图请先检查是否包含个人桌面信息。
