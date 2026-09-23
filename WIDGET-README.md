# GearPulse 桌面小组件

WPF 卡片位于主屏右下角、任务栏上方。它不抢焦点，不占任务栏或 Alt+Tab；普通窗口可遮挡它。卡片每 10 秒读取一次设备，接收器拔出后隐藏对应鼠标行，并根据行数调整高度。托盘菜单提供显示／隐藏、开机启动和退出。

## 命令

```powershell
# 安装 .NET 10 SDK 后构建自带运行时的 win-x64 EXE。
pwsh -NoProfile -File .\Publish-GearPulse.ps1

# 直接运行新版本，先检查桌面显示与托盘菜单。
.\publish\win-x64\GearPulse.exe

# 切换当前用户登录任务到新 EXE，并立即启动。
pwsh -NoProfile -File .\Install-BatteryWidget.ps1

# 停止两种版本的卡片，但保留登录任务。
pwsh -NoProfile -File .\Stop-BatteryWidget.ps1

# 停止并移除 GearPulse 登录任务。
pwsh -NoProfile -File .\Remove-BatteryWidget.ps1

# 如需回退到旧版 PowerShell/WinForms 卡片。
pwsh -NoProfile -File .\Install-LegacyBatteryWidget.ps1
```

托盘“开机启动”切换当前用户的 `GearPulse` 计划任务。直接运行 EXE 而尚未安装登录任务时，该菜单项不可用。新程序的日志位于 `%LOCALAPPDATA%\GearPulse\gear-pulse.log`；开发测试时可设置 `GEARPULSE_DATA_DIR` 将日志放在指定目录。旧版设备诊断脚本和 `Start-BatteryWidget.ps1` 仍保留，但不再作为正式入口。

发布目录应保持在固定位置，登录任务通过该路径启动 EXE。迁移脚本在 `state\gear-pulse-task-before-wpf.xml` 保存原 `GearPulse` 任务定义，并禁用旧名称 `BlackShark Battery Widget` 的登录任务以避免重复卡片；回退脚本会重新注册旧版 `GearPulse` 任务。Wallpaper Engine 桥接任务由用户原有备份负责恢复，安装程序不会修改壁纸。

## 测试范围

离线测试覆盖状态文案、设备顺序、隐藏行、协议解析、采样防重入和托盘命令。迁移后应现场检查 DPI 缩放、普通窗口遮挡、Explorer 重启、充电变化及实际注销再登录。其他 ATK 型号不在当前支持范围内。
