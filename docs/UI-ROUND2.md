# 第二轮 UI 优化

日期：2026-10-10。

- 页面标题与说明集中在同一标题组，间距为 6 DIP。
- 记录状态使用原生 InfoBar：记录时为 Success 状态、绿色播放图标与“ClipFlow 正在记录”；暂停时为 Warning 状态、黄色暂停图标、停止保存的说明与继续记录按钮。未能初始化时显示 Error 状态，禁用记录按钮。
- 本机保存提示与设置的图标、文字分别左对齐，缩短两者之间的距离。
- 设置页删除指定的敏感内容说明文案；隐私标记跳过逻辑仍然有效。
- 收藏夹不显示清空历史按钮，支持逐条取消收藏或删除。
- 没有本地历史时，最近记录和设置页的清空历史按钮禁用。该状态依据全部本地历史，搜索或类型筛选结果为空不会影响它。
- 移除独立图片导航页。最近记录包含文字和图片，并保留全部／文本／图片筛选。

Debug 构建及 11 组真实剪贴板／WinUI 集成检查、1 组完整进程重启检查通过。现有集成场景增加了状态条图标与 Severity、空历史按钮、收藏夹清空入口隐藏、导航项目及图片筛选的验证。

截图由实际 WinUI 控件渲染；不包含系统标题栏按钮和 compositor Mica 材质。详见 [验证范围](VALIDATION.md)。

- 深色记录状态（本地验收截图：`screenshots/recent-Dark.png`）
- 暂停状态（本地验收截图：`screenshots/recent-paused.png`）
- 窄窗口暂停状态（本地验收截图：`screenshots/recent-paused-narrow.png`）
- 空历史与禁用清空按钮（本地验收截图：`screenshots/recent-empty.png`）
- 收藏夹与重启恢复（本地验收截图：`screenshots/restart.png`）

控件参考：[Microsoft Learn — InfoBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar)。本次未升级依赖。
