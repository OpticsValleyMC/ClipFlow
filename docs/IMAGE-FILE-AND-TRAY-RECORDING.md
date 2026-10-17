# 图片文件本地副本与托盘记录开关

日期：2026-10-11。版本 1.0.0.0，作者 OpticsValley。

## 图片副本

数据继续位于当前用户的 `%LocalAppData%\ClipFlow\images`。该目录由 ClipFlow 管理；安装目录、QQ/微信缓存和系统临时目录都不是历史记录的来源依赖。

此前位图剪贴板已经解码并写入独立文件，本轮补充 StandardDataFormats.StorageItems 图片文件接收。文件立即读取、解码并生成 PNG 原图和缩略图；数据库仅保存 `images/<hash>.png` 等相对路径。存储层拒绝没有图片字节、仅带来源路径的图片记录。原图与缩略图沿用独立文件、内容哈希去重及删除/清空/数量裁剪清理机制。

支持 PNG、JPEG、BMP、GIF、TIFF、WebP、JXR 文件扩展名，实际可解码性取决于 Windows 位图解码器。列表最多检查前 16 个文件，只采集首个可解码的支持图片，不遍历文件夹、不下载网络图片。非临时位图读取失败时可尝试同一剪贴板提供的图片文件；非图片和损坏文件跳过，临时访问错误继续沿用有限重试，取消与暂停仍立即生效。尺寸、字节数及读取时间上限保持原策略。

## 托盘开关

菜单为版本信息、打开 ClipFlow、暂停/继续记录、退出。每次打开菜单读取当前视图模型状态，显示正确操作；初始化未就绪时禁用记录选项。选择后调用现有 ToggleRecordingCommand，不打开主窗口。界面 InfoBar、设置页开关和托盘共用同一设置，保存到 SQLite；暂停期间不保存新剪贴板内容，恢复不会导入暂停期间的旧内容。

沿用已修复的独立菜单所有者窗口、延后弹出、重入保护及菜单循环结束后派发命令。

## 实际验证

隔离运行目录：`artifacts/integration-66f66e7c51014977ac9fe39f2c9e18d3`。

- 27 项核心检查通过，新增仅带外部图片路径的输入被拒绝且不写入记录。
- 15 组 WinUI / 真实 Windows Clipboard 集成检查通过。新增临时 PNG 文件测试：文件列表中先放非图片和损坏 PNG，再放有效图；正确采集、去重，保存受管相对路径；删除原临时文件并重开数据库后，详情解码正确、再次复制正确，删除记录清理原图与缩略图。
- 新进程重启检查通过。
- 托盘实际鼠标点击暂停和继续通过：主窗口保持隐藏，暂停期间内容不保存，开关状态入库，菜单与视图模型/界面状态同步；菜单持续显示、取消重开、恢复、第二实例、任务栏重建及退出码 0 通过。
- Debug 构建 0 警告、0 错误；Release 使用独立构建目录重新发布，文件元数据保持 1.0.0.0 / OpticsValley。旧版正常用户实例正在运行，未退出该实例，也未把启动新版后激活旧实例计为新版冒烟通过。

本轮报告：[integration-results.txt](integration-results.txt)、[restart-results.txt](restart-results.txt)、[tray-results.txt](tray-results.txt)。[release-smoke-results.txt](release-smoke-results.txt) 保留上一轮菜单闪退修复的正式进程报告；本轮未重复 Release 进程冒烟，原因如上。使用模拟截图提供者的真实 Windows 数据包验证，未针对每个 QQ/微信版本逐一操作界面。

API 核对：[GetStorageItemsAsync](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackageview.getstorageitemsasync?view=winrt-26100)。Context7 当前无可调用工具，使用微软官方文档。
