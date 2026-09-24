# GearPulse 桌面小组件

当前发布版本：**V1.2**（产品版本 `1.2.0`，Windows 文件版本 `1.2.0.0`）。同一发布包包含外观设置、通用 ATK／VXE／VGN 设备支持和简体中文、English、繁體中文界面。

WPF 卡片位于主屏右下角、任务栏上方。它不抢焦点，不占任务栏或 Alt+Tab；普通窗口可遮挡它。卡片每 10 秒读取一次电量并重新发现 ATK／VXE／VGN USB 外设，罗技设备每 45 秒重新发现，接收器拔出后隐藏对应行。多行超出屏幕可用高度时可以滚动。支持 BlackShark V2 Pro、ATK／VXE／VGN 的 2.4G 与有线 USB 设备，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。没有已验证电量协议的设备显示“电量暂不可用”。托盘菜单提供显示／隐藏、开机启动和退出。

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

托盘“开机启动”切换当前用户的 `GearPulse` 计划任务。直接运行 EXE 而尚未安装登录任务时，该菜单项不可用。托盘“语言”菜单可在简体中文、English 和繁體中文之间切换。托盘“外观设置”可即时预览线条或剪影图标、小／中／大尺寸、背景和文字图标各自的透明度，以及显示器与四角位置。默认仍是主屏右下角的现有外观；选中的显示器断开后，卡片暂回主屏，重新连接后恢复。语言和外观保存在 `%LOCALAPPDATA%\GearPulse\settings.json`，下次启动沿用。日志位于 `%LOCALAPPDATA%\GearPulse\gear-pulse.log`；开发测试时可设置 `GEARPULSE_DATA_DIR` 将日志和设置放在指定目录。旧版设备诊断脚本和 `Start-BatteryWidget.ps1` 仍保留，但不再作为正式入口。

发布目录应保持在固定位置，登录任务通过该路径启动 EXE。迁移脚本在 `state\gear-pulse-task-before-wpf.xml` 保存原 `GearPulse` 任务定义，并禁用旧名称 `BlackShark Battery Widget` 的登录任务以避免重复卡片；回退脚本会重新注册旧版 `GearPulse` 任务。Wallpaper Engine 桥接任务由用户原有备份负责恢复，安装程序不会修改壁纸。

## 测试范围

离线测试覆盖状态文案、设备顺序、隐藏行、ATK 多接口去重与品牌识别、协议解析、采样防重入和托盘命令。迁移后应现场检查 DPI 缩放、普通窗口遮挡、Explorer 重启、充电变化及实际注销再登录。ATK／VXE／VGN 设备会动态发现；只有已验证的鼠标协议提供电量，其他设备显示未知电量。蓝牙不在当前范围内。
