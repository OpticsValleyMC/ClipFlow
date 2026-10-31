# ClipFlow

[![从 Microsoft Store 下载](https://get.microsoft.com/images/zh-cn%20dark.svg)](https://apps.microsoft.com/detail/9PL1LGM11WGM?hl=zh-cn&gl=CN&ocid=pdpshare)

[![Windows 10 / 11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)](#支持的系统)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

复制了新内容，上一条却找不到了？ClipFlow 会帮你把复制过的文字和图片留在电脑里，需要时再找出来

打开软件后，你照常复制就行。常用的内容可以收藏，图片可以放大查看，关掉窗口后也能继续在后台记录

**当前版本：1.1.0.0 · 作者：OpticsValley**

## 能做什么

- **找回复制过的内容**：文字和图片都在「最近记录」里，重启软件也还在
- **收藏常用内容**：点一下星标，下次去「收藏夹」就能找到；还可以置顶，按复制时间或内容名称排序
- **快速查找和再复制**：输入关键词找文字，或筛选图片；点记录上的复制按钮，再到需要的地方粘贴
- **看完整文字和原图片**：长文字可以展开查看；图片支持滚轮放大、缩小，放大后按住鼠标左键拖动
- **随时暂停**：不想记录时，在主窗口、设置或托盘菜单里暂停，之后再继续
- **选择喜欢的外观**：浅色、深色，或跟随系统，切换后立即生效
- **调整背景效果**：云母支持轻柔、标准、明显三档，也可以关掉材质，使用纯色背景
- **开机自动运行**：安装版可在设置页或托盘菜单里开启，登录 Windows 后在后台运行，不弹出主窗口

## 支持的系统

Windows 10（2004 及以上版本）和 Windows 11

Windows 11 支持云母背景，Windows 10 使用与主题一致的普通背景

## 下载和打开

ClipFlow 已上架 Microsoft Store：[点击下载「ClipFlow - 剪贴板工具」](https://apps.microsoft.com/detail/9PL1LGM11WGM?hl=zh-cn&gl=CN&ocid=pdpshare)

也可以打开 Microsoft Store，搜索「ClipFlow - 剪贴板工具」下载

软件从打开后发生的复制开始记录，不会自动收集启动前的内容。暂停期间复制的内容，也不会在恢复后补存

搜索目前能查找文字内容，找图片时可以使用类型筛选

## 关掉窗口后，还会记录吗？

会

点窗口右上角的关闭按钮后，ClipFlow 会在系统托盘显示，软件会在后台继续运行

- 点击托盘里的 ClipFlow 图标，可以重新打开窗口
- 右键图标，可以打开软件、暂停或继续记录，也能设置开机自启动；菜单使用亚克力背景，不支持时自动使用纯色
- 想彻底关闭软件，右键图标，选择**退出**

如果系统托盘不可用，关闭窗口会直接退出

开机自启动默认关闭，由 Windows 管理，也可以在任务管理器的「启动应用」中调整。便携版没有安装身份，不提供这个开关

## 内容存在哪里？

**只存在你的电脑上，不上传，也不做云端同步。** 保存过的图片会保留自己的副本

默认保留最近 **500 条**，可以在设置里调整。超过上限会删除最旧的记录，收藏和置顶也计入这个上限

复制密码、验证码等不想留存的内容前，建议先暂停记录。详细说明见 [隐私政策](PRIVACY.md)。

## 反馈和联系作者

遇到问题或有想法，欢迎在本仓库的 **Issues** 里留言。说明你的 Windows 版本、ClipFlow 版本，以及怎样操作会出现问题

作者：OpticsValley

- 邮箱：[opticsvalley@qq.com](mailto:opticsvalley@qq.com)

## 想参与贡献/开发？

项目使用 C#、WinUI 3 和 Windows App SDK 2.2.0。开发需要 Windows、.NET 8 或更新版本；用 Visual Studio 时，安装 Windows 应用开发相关工具。

下载源码后，在项目文件夹中打开 PowerShell，执行：

```powershell
.\scripts\Build.ps1 -Run
```

第一次构建需要联网下载依赖。运行基础测试：

```powershell
.\scripts\Test.ps1
```

提交修改前请先阅读 [贡献指南](CONTRIBUTING.md)，商店打包说明放在 [发布指南](docs/STORE-RELEASE.md)，普通用户不需要看哦

## 开源许可

ClipFlow 使用 [MIT 许可证](LICENSE)。欢迎使用、修改和分享，记得保留原有版权及许可声明。第三方组件的许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
