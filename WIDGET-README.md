# GearPulse 桌面小组件

当前本机发布目录仍为 **V1.2**；**V1.3** 已通过实机验证；新增“隐藏无法读取的信息”的独立候选版本为 **V1.3.5**（产品版本 `1.3.5`，Windows 文件版本 `1.3.5.0`），位于 `publish/1.3.5-hide-unreadable-candidate/GearPulse.exe`。同一发布包包含通用雷蛇设备发现、外观设置、通用 ATK／VXE／VGN 设备支持和简体中文、English、繁體中文界面。用户已确认 Viper V4 Pro 能显示型号和电量，但充电状态不可读；其他雷蛇型号仍需在对应实机验证。

对应的当前用户安装包为 `publish/GearPulse-1.3.5-Setup.exe`，可直接在 Windows x64 电脑上运行；打包本身没有修改当前本机安装。安装包复用原有 GearPulse 安装身份，覆盖安装时保留已有设置。

WPF 卡片位于主屏右下角、任务栏上方。它不抢焦点，不占任务栏或 Alt+Tab；普通窗口可遮挡它。卡片每 10 秒读取一次电量并重新发现当前 Windows 耳机、雷蛇鼠标／键盘／耳机与 ATK／VXE／VGN USB 外设，罗技 HID++ 设备每 45 秒重新发现，接收器拔出后隐藏对应行。多行超出屏幕可用高度时可以滚动。支持 BlackShark V2 Pro、G522 LIGHTSPEED 接收器、ATK／VXE／VGN 设备，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。雷蛇 USB／接收器设备仅在型号和协议接口明确匹配时读取电量；已连接的雷蛇蓝牙设备使用 Windows 电量值。没有可信电量来源时显示系统名称和“电量暂不可用”。3.5 mm 模拟耳机可能只有声卡接口名称。没有任何设备时显示“未发现设备”。托盘菜单提供显示／隐藏、开机启动和退出。

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

托盘“开机启动”切换当前用户的 `GearPulse` 计划任务。直接运行 EXE 而尚未安装登录任务时，该菜单项不可用。托盘“语言”菜单可在简体中文、English 和繁體中文之间切换。托盘“外观设置”可即时预览线条或剪影图标、小／中／大尺寸、背景和文字图标各自的透明度，以及显示器与四角位置，并可分别开启／关闭有线和蓝牙耳机行；两项默认开启，不影响 LIGHTSPEED。“隐藏无法读取的信息”默认关闭；开启后保留设备名称和图标，只显示可读取的电量及充电状态，没有可读电量时收起状态文字。默认仍是主屏右下角的现有外观；选中的显示器断开后，卡片暂回主屏，重新连接后恢复。语言和外观保存在 `%LOCALAPPDATA%\GearPulse\settings.json`，下次启动沿用。日志位于 `%LOCALAPPDATA%\GearPulse\gear-pulse.log`；开发测试时可设置 `GEARPULSE_DATA_DIR` 将日志和设置放在指定目录。旧版设备诊断脚本和 `Start-BatteryWidget.ps1` 仍保留，但不再作为正式入口。

发布目录应保持在固定位置，登录任务通过该路径启动 EXE。迁移脚本在 `state\gear-pulse-task-before-wpf.xml` 保存原 `GearPulse` 任务定义，并禁用旧名称 `BlackShark Battery Widget` 的登录任务以避免重复卡片；回退脚本会重新注册旧版 `GearPulse` 任务。Wallpaper Engine 桥接任务由用户原有备份负责恢复，安装程序不会修改壁纸。

## 测试范围

Viper V4 Pro 有线 `1532:00E5`、无线 `1532:00E6` 使用已公开实机读取器所证实的 `MI_03`／`MI_04` 非输入接口和只读电量查询。型号名按产品 ID 显示，充电状态暂不推断。可在另一台 Windows x64 电脑直接运行 `publish/1.3.4-viper-v4-diagnostic/GearPulse.Smoke.exe --razer-integration`；它输出脱敏产品 ID、接口编号、报文长度、接口是否合格和读取结果，不输出设备路径或序列号。协议依据：[OpenRazer 设备登记](https://github.com/openrazer/openrazer/issues/2760)、[Viper V4 Pro 实机读取实现](https://github.com/Riqqqque/RazerBatteryDisplay/blob/main/src/battery.rs)。

离线测试覆盖状态文案、设备顺序、隐藏行、ATK 多接口去重与品牌识别、G522 报文解析、耳机去重、开关存储、采样防重入和托盘命令。迁移后应现场检查 DPI 缩放、普通窗口遮挡、Explorer 重启、充电变化及实际注销再登录。ATK／VXE／VGN 设备会动态发现；只有已验证的鼠标协议提供电量，其他设备显示未知电量。蓝牙耳机可列出，但尚无通用电量读取器。

### G522 异地实机检查

将 `publish/headset-candidate/GearPulse.exe` 和 `publish/headset-diagnostics/GearPulse.Smoke.exe` 发送给装有 G522 的 Windows 电脑。连接 LIGHTSPEED 接收器并打开耳机，在 PowerShell 中运行诊断程序所在目录的 `./GearPulse.Smoke.exe --g522-watch`。该命令每 2 秒记录一次型号、状态、电量和充电状态，共约 30 秒；输出不含设备序列号。分别在正常连接、耳机关机、重新开机、拔出接收器四种情况下运行，检查旧百分比不会残留。再运行候选卡片，确认只显示一条 G522 耳机记录。有线 USB 和蓝牙模式的 G522 不属于此电量协议的验证范围。实机检查通过前不要覆盖正在使用的 `publish/win-x64/GearPulse.exe`。

### ATK A9 Mini+ 识别检查

用户实测结果：有线模式下，A9 Mini+ 的鼠标型号、电量和充电状态均正常；接收器模式下，界面显示 `ATK 8K Dongle`，电量和充电状态正常。接收器模式尚未确认能识别配对鼠标为 A9 Mini+，因此仍保留中性接收器名称。

`dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release -- --atk-diagnostics` 列出 ATK 接收器 ID、接口规格，以及已验证 17 字节接口的只读在线、身份和电量查询结果；不输出设备路径或序列号，不对其他接口发送命令。A9 Mini+ 通过 2.4G 连接并唤醒后，检查其 `CID/MID`、状态和电量，并与 ATK HUB 对照。所有已收录的 ATK／VXE／VGN 鼠标 ID 只用于发现设备；能从设备描述识别出的接收器显示中性名称，不根据接收器 ID 或带型号的接收器描述推断当前配对鼠标。只有已验证的鼠标身份应答才显示对应无线型号，休眠或查询失败时清除旧型号和电量。无法从描述区分接收器与有线鼠标的型号仍需实机核对。通过 USB 数据线连接时，检查 A9 Mini+ 显示鼠标图标。未取得有效无线应答前不为 A9 Mini+ 添加推测的身份映射或电量协议。
