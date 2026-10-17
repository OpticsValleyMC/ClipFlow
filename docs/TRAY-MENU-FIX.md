# 托盘右键菜单修复

日期：2026-10-10。版本 1.0.0.0，作者 OpticsValley。

托盘菜单原先在 Shell 回调内部直接弹出，以可能已经隐藏的 WinUI 主窗口作为菜单所有者，同时接受版本 4 的 WM_CONTEXTMENU 和旧版右键释放通知。缺少重入保护；之前测试只观察菜单进入过菜单循环，无法排除闪现后关闭。

现在使用独立的原生工具窗口承载菜单，在屏幕外临时显示并取得前台焦点，不显示任务栏或 Alt+Tab 项。主窗口继续独立隐藏或显示。菜单请求通过消息延后执行；版本 4 只接受对应图标的 WM_CONTEXTMENU，忽略旧版右键释放通知，并拒绝已排队或正在显示时的重复请求。

TrackPopupMenuEx 使用 TPM_RETURNCMD / TPM_NONOTIFY，退出菜单循环、隐藏辅助窗口并释放菜单后，再向主窗口派发选择的命令。取消时返回托盘焦点；选择“打开”时保留恢复后的应用焦点。辅助窗口随 TrayService 释放。

## 回归证据

实际 Windows 11 原生菜单专项测试报告：[tray-results.txt](tray-results.txt)。隔离目录：`artifacts/tray-menu-e1e93db74dff401fb0ecb192d9590326`。

- 隐藏主窗口后剪贴板继续记录。
- 菜单持续显示至少一秒，期间注入重复上下文通知和旧版右键释放通知，菜单保持打开。
- 两次取消、再次打开均成功。
- GetMenuItemRect 获取实际菜单项屏幕位置，检查菜单持有前台焦点后，通过系统鼠标输入实际点击“打开 ClipFlow”；核对原生菜单返回 OpenCommand，并确认主窗口恢复。
- 主窗口显示时菜单持续显示，取消不会改变主窗口可见性。
- 单击托盘恢复、第二进程激活已有实例、任务栏重建、退出码 0 通过。

26 项核心检查通过。本轮另一次完整运行中，14 组剪贴板 / WinUI 集成及重启检查通过；后续复跑出现剪贴板采集等待超时，保留在对应隔离目录，不计为通过。对本次菜单修复，使用独立托盘专项完成最终验证。最初用投递按键消息模拟选择未成功；最终改用上述真实鼠标点击。

正式版本重新发布后运行 `scripts/Smoke-Release.ps1`，结果见 [release-smoke-results.txt](release-smoke-results.txt)。该冒烟验证启动、隐藏、恢复和退出；菜单持续显示及点击由运行相同菜单实现的 Debug 隔离实例完成。

API 依据：[TrackPopupMenuEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenuex)、[Shell_NotifyIconW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)、[GetMenuItemRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmenuitemrect)。Context7 当前没有可调用工具，使用微软官方文档核对。
