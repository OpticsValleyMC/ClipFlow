# 图片鼠标拖动、版本与正式图标

## 图片拖动

放大后，按住鼠标左键可在预览区平移图片，水平与垂直偏移均以固定视口中的鼠标坐标计算，避免缩放后的坐标误差。偏移限制在图片边界内；松开、指针捕获丢失、取消操作或关闭详情时释放捕获。触摸输入继续使用 ScrollViewer 原生平移和缩放，键盘与滚动条操作保留。适应窗口和 100% 会先结束拖动。

使用 computer-use 技能的真实 Windows 鼠标输入，在隔离的 1920×1440 图片、200% 放大状态下完成四次拖动：

| 操作 | 最终水平 / 垂直偏移 | 指针已释放 |
| --- | --- | --- |
| 向左上拖动 | 280 / 240 | 是 |
| 反向拖动 | 120 / 120 | 是 |
| 拖出预览区并松开 | 0 / 0（夹限到边界） | 是 |
| 再次向左上拖动 | 170 / 150 | 是 |

原始鼠标样本见 `mouse-pan-results.txt`。该次交互夹具在四次操作后仍等待手动关闭，达到五分钟退出计时，因此末尾记录的是夹具超时；不是应用拖动异常。自动化随后被用户按 Esc 停止，未继续发送桌面输入。夹具现已改为收集四次完整拖动后自动关闭。没有将未执行的后续按钮点击算作测试通过。

## 版本和作者

`ClipFlow/Directory.Build.props` 统一设置版本、程序集版本、文件版本与产品版本为 `1.0.0.0`，Authors、Company 为 `OpticsValley`。关于页与托盘通过 AppInfo 读取程序集版本，不再分别硬编码三段版本号。MSIX 清单版本为 `1.0.0.0`，PublisherDisplayName 为 OpticsValley。

## 图标

使用内置 ImageGen，以原托盘蓝色双轮廓图标为参考进行保守优化。保留两枚错位的圆角矩形空心轮廓、蓝色和重叠方向，仅调整边缘、笔画与留白。未采用先前被用户拒绝的实心剪贴板设计。生成提示完整保存于 `icon-imagegen-prompt.txt`。

- 源文件：`ClipFlow/ClipFlow/Assets/ClipFlow.master.png`，透明高分辨率 PNG。
- exe 图标：`ClipFlow.ico`，15 个尺寸，16–256 像素。
- 窗口标题：256 像素 PNG 缩小显示，窗口任务栏和 Alt+Tab 使用 AppWindow.SetIcon。
- 托盘：从相同 ICO 按当前 DPI 加载原生 HICON，退出时释放。
- MSIX 资源：44、71、150、310 方形图标，宽图、StoreLogo、启动图；100%、125%、150%、200%、250%、300%、400% 变体。
- 任务栏精确尺寸：15 种尺寸，默认、unplated、lightunplated 三套，透明背景。

格式导出命令（Windows PowerShell 5.1）：

```powershell
powershell.exe -NoProfile -File scripts/Build-IconAssets.ps1
powershell.exe -NoProfile -File scripts/Test-BrandAssets.ps1
```

已核对 exe 文件/产品版本、公司、项目作者、清单版本、PNG 尺寸/透明度和 Windows ICO 解码。结果见 `brand-results.txt`。图标资源按 [微软图标构建文档](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction) 配置。

当前交付仍为 unpackaged 发布目录。Microsoft Store 发布时，Package Identity 的 Name/Publisher 必须替换为 Partner Center 为该产品提供的实际值，并完成相应打包、签名和认证；图标配置不等于已经通过 Store 审核。
