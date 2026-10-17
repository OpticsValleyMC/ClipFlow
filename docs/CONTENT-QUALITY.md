# 文本与图片预览优化

日期：2026-10-10。

空状态说明在正常窗口下显示为完整的一行；窄窗口保持单行，空间不足时省略并可通过悬浮提示查看完整说明。

文本保存维持最多 200 万字符的上限。列表只排版约 512 字符的预览，显示全文字数与查看全文入口。详情支持选择、滚动；超过 16,000 字符时提供上一页／下一页，保持全部原文及 Unicode 代理对、CRLF 边界。“复制”使用数据库中的完整原文，不受当前页或列表预览影响。

新图片缩略图最长边提升为 1024 像素。点击图片后直接从本地原图加载完整像素，支持适应窗口、100% 原始像素、放大／缩小和滚动。原图仅在详情打开时加载，关闭后释放视图引用。损坏、缺失或超过尺寸／大小限制的文件显示友好占位。旧记录的原图可直接高清查看，旧缩略图在重新复制同一图片时更新。

标准测试命令（独立构建目录避免运行中的旧应用锁定 Debug 文件）：

```powershell
& .\scripts\Test.ps1 -Integration -BuildOutputDirectory 'artifacts/quality-round-debug'
```

26 项核心检查、13 组集成检查和 1 组独立进程重启检查通过。此次实际打开长文／图片对话框，覆盖滚动、全文复制、首尾页、最小窗口、高清原图与损坏文件。详细结果与截图见 [验收报告](VALIDATION.md)。

官方 API 参考：[TextBox.MaxLength](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textbox.maxlength?view=windows-app-sdk-1.8)、[BitmapImage 解码像素](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.bitmapimage.decodepixeltype?view=windows-app-sdk-1.8)、[ScrollViewer.ChangeView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer.changeview?view=windows-app-sdk-1.8)。本轮未升级依赖。
