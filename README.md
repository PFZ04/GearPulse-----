# GearPulse｜外设脉动

## 项目简介

GearPulse 是一个面向 Windows 的桌面外设电量小组件。它把已连接的鼠标、键盘和耳机汇总到一张可被普通窗口遮挡的小卡片中，每台设备独立显示名称、图标、真实电量百分比，以及能够确认的充电状态。卡片默认位于主屏右下角；托盘菜单提供显示与隐藏、登录时启动、语言和外观设置。

程序直接从 Windows 设备信息、蓝牙电量属性及明确支持的 USB HID 协议读取数据，无需依赖 Synapse、G HUB 或 ATK HUB。它不修改壁纸，也不安装驱动。无法可靠读取的数值保持未知，设备休眠、断开或查询失败时不会沿用旧百分比；可在外观设置中隐藏这些未知信息的文字，同时保留设备名称和图标。

项目以 .NET 10 WPF 编写，提供自带运行时的独立 EXE 和当前用户安装包。设备发现、电量读取、卡片显示与托盘控制由同一程序完成；PowerShell 只用于构建、安装辅助和诊断，不需要常驻运行。

## 当前版本

最新候选版为 **V1.3.5**：独立程序位于 `publish\1.3.5-hide-unreadable-candidate\GearPulse.exe`，安装包位于 `publish\GearPulse-1.3.5-Setup.exe`。安装包已编译，尚未在本机运行；本机当前安装版仍为 **V1.2.0**。Viper V4 Pro 的型号和电量已由用户实机验证，充电状态不可读；其他雷蛇型号的通用电量读取尚未实机验证。

## 设备支持

当前支持发现 Razer 鼠标、键盘和耳机，通过 USB 接收器或有线 USB 连接的 ATK／VXE／VGN 外设，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。发现设备不代表一定能读取其电量；具体行为如下。

| 设备 | 识别方式 | 行为 |
|---|---|---|
| BlackShark V2 Pro | `1532:0555` | 电量、充电状态和接收器状态。 |
| Razer Viper V4 Pro | 有线 `1532:00E5`、无线 `1532:00E6` | 用户实测可显示型号与电量；充电状态未知。 |
| Razer 鼠标／键盘／耳机 | Windows USB 或已连接蓝牙设备 | 自动发现、按设备容器去重；部分明确支持的 USB／接收器鼠标和键盘使用只读 HID 查询，蓝牙使用 Windows 提供的电量值，其他设备显示“电量暂不可用”。无法确认配对型号的接收器使用接收器名称。 |
| ATK／VXE／VGN 鼠标、键盘、耳机 | USB 设备容器、厂商信息或已收录的产品 ID | 自动发现并去重；产品 ID 只用于发现设备，不直接决定鼠标型号。有已验证电量协议时读取百分比，否则显示“电量暂不可用”。 |
| ATK F1 V3 Ultimate+、A9 Plus NK | 接收器 `373B:1278`、`373B:10C9` 与鼠标身份应答 | 保留已实测的在线、型号和电量查询；无线型号需要有效身份应答，休眠或查询失败时清除旧型号和电量。 |
| ATK A9 Mini+ | 有线 USB 产品名称；接收器模式显示 `ATK 8K Dongle` | 用户实测：有线模式正常识别鼠标型号、显示电量和充电状态；接收器模式正常显示电量和充电状态，但界面显示接收器名称，尚未识别为 A9 Mini+。 |
| 罗技 LIGHTSPEED 鼠标／键盘 | HID++ 接收器与配对设备身份 | 动态读取设备名称、类型和电量功能；休眠时清除旧百分比。 |

### 其他电脑的用户实测记录

2026-09-24，用户报告已在其他电脑实测 **Logitech G PRO X2 SUPERSTRIKE** 和 **Logitech G502 X LIGHTSPEED**；两款鼠标的电量显示、充电状态和休眠处理均正常。具体连接方式、读取的百分比数值和拔插接收器结果未提供。详细记录见本机的 `WIDGET-VERIFICATION.md`。

用户还报告 **ATK A9 Mini+**：有线模式下型号、电量和充电状态正常；接收器模式下界面显示 `ATK 8K Dongle`，电量和充电状态正常。接收器模式的鼠标型号识别仍未确认。

用户已确认 **Razer Viper V4 Pro** 可显示正确型号和电量，但无法读取充电状态。实测时的连接方式、电量数值、休眠和接收器拔插结果尚未提供。

## 项目组成

- `src/GearPulse/`：WPF 卡片、托盘与设置，以及设备发现和电量提供器。
- `tests/GearPulse.Smoke/`：离线状态、协议和设置检查；`tests/GearPulse.VisualPreview/` 用于视觉预览。
- `installer/`：Inno Setup 安装包定义及登录任务辅助脚本。
- `Publish-GearPulse.ps1` 与 `Build-Installer.ps1`：分别生成独立 EXE 和安装包。

## 构建与安装

普通用户可直接运行 `publish\GearPulse-1.3.5-Setup.exe` 安装 1.3.5 候选版，无需安装 .NET SDK。安装向导默认勾选登录时启动；可取消，之后仍可从托盘切换。安装包只为当前 Windows 用户安装，并在卸载时询问是否清除设置与日志。当前本机仍使用 1.2.0，尚未运行新版安装包。制作安装包的步骤见 [安装包说明](installer/README.md)。

需要从源码重新生成 V1.3.5 程序和安装包时，在 Windows x64 上安装 .NET 10 SDK 和 Inno Setup 7，然后在仓库目录运行：

```powershell
pwsh -NoProfile -File .\Publish-GearPulse.ps1 -OutputPath .\publish\1.3.5-hide-unreadable-candidate
pwsh -NoProfile -File .\Build-Installer.ps1 -SourceExe .\publish\1.3.5-hide-unreadable-candidate\GearPulse.exe -ExpectedVersion 1.3.5
```

V1.3.5 的“外观设置”新增“隐藏无法读取的信息”，默认关闭。开启后，设备名称和图标仍显示；电量可读但充电状态未知时只显示百分比，电量也不可读时收起状态文字。该设置会保存并即时生效。现有 `publish\win-x64\GearPulse.exe` 仍是本机使用的 V1.2.0；构建候选版或安装包不会替换它。详细操作见 [WIDGET-README.md](WIDGET-README.md)。

## 验证

```powershell
dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release
pwsh -NoProfile -File .\Test-BlackSharkProtocol.ps1
```

离线测试不发送 HID 命令。可用 `dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release -- --atk-diagnostics` 查看 ATK 接收器 ID、接口规格、身份码、查询状态和电量；只查询已验证的 17 字节接口，不输出设备路径或序列号。V1.3.5 已通过 172 项离线检查；可用 `--razer-integration` 查看本机已发现的雷蛇设备名称、类型、产品 ID、接口编号、报文长度和电量，不输出设备路径或序列号。用户已确认 Viper V4 Pro 能显示型号和电量，但读不到充电状态；连接模式、休眠和拔插接收器结果尚未提供。状态不明时不沿用旧电量，也不把未知电量当作 0%。

ATK 系列发现范围是能够从 Windows USB 设备信息确认品牌的 ATK／VXE／VGN 设备，以及已收录产品 ID 的鼠标；蓝牙不在范围内。共用 Compx 芯片的其他品牌不会仅凭厂商 ID 被误认。可识别的接收器显示中性名称，不采用接收器 ID 或带型号的接收器描述来判断配对鼠标；无法从描述区分接收器与有线鼠标的设备仍需实机核对。17 字节鼠标协议匹配时才查询电量；其他协议的型号（包括 ATK Zero）目前显示未知电量。A9 Mini+ 接收器模式已由用户确认能显示电量和充电状态，但仍显示 `ATK 8K Dongle`；无线身份需取得有效应答后才能标为 A9 Mini+，不能借用 F1 的身份映射推测。已收录的鼠标 ID 参考 [Mouse Tray 的协议与型号表](https://github.com/Fan4Metal/mouse_tray/tree/master/mouse_tray/drivers/chipset)。

罗技支持限于能通过接收器的 HID++ 2.0 接口确认型号、名称、类型与真实电量百分比的无线设备；仅有电压或粗略档位的设备不会显示。雷蛇只对 [OpenRazer 鼠标](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c)与[键盘](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)电量查询明确列出的产品 ID，以及 [Viper V4 Pro 实机读取实现](https://github.com/Riqqqque/RazerBatteryDisplay/blob/main/src/battery.rs)确认的 `00E5`／`00E6` 发送只读电量查询；必须符合预期 HID 接口和有效应答才显示百分比。Viper V4 Pro 的型号和电量已由用户实测，充电状态保持未知。耳机除 BlackShark V2 Pro 外当前仅做通用发现；蓝牙仅在 Windows 报告连接状态和电量时显示百分比。雷蛇其他通用协议和蓝牙读取目前只有离线测试，其他型号尚未实机验证。BlackShark 协议参考 [OpenRazer PR #2862](https://github.com/openrazer/openrazer/pull/2862)，罗技 HID++ 功能参考 [Logitech 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)。本项目采用 [GPL-2.0-or-later](LICENSE)；本机日志、截图和原始 HID 报文不纳入仓库。
