# GearPulse｜外设脉动

当前安装包版本：**V1.2**（产品版本 `1.2.0`，Windows 文件版本 `1.2.0.0`）。**V1.3** 已通过实机验证；最新独立候选程序为 **V1.3.2**（产品版本 `1.3.2`，Windows 文件版本 `1.3.2.0`），位于 `publish\1.3.2-candidate\GearPulse.exe`。

Windows 桌面电量小组件，显示 Razer BlackShark V2 Pro、通过 USB 接收器或有线 USB 连接的 ATK／VXE／VGN 外设，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。正式程序是 .NET 10 WPF 独立 EXE，不需要 PowerShell 常驻运行；PowerShell 仅用于构建、安装和诊断。

| 设备 | 识别方式 | 行为 |
|---|---|---|
| BlackShark V2 Pro | `1532:0555` | 电量、充电状态和接收器状态。 |
| ATK／VXE／VGN 鼠标、键盘、耳机 | USB 设备容器、厂商信息或已收录的产品 ID | 自动发现并去重；产品 ID 只用于发现设备，不直接决定鼠标型号。有已验证电量协议时读取百分比，否则显示“电量暂不可用”。 |
| ATK F1 V3 Ultimate+、A9 Plus NK | 接收器 `373B:1278`、`373B:10C9` 与鼠标身份应答 | 保留已实测的在线、型号和电量查询；无线型号需要有效身份应答，休眠或查询失败时清除旧型号和电量。 |
| ATK A9 Mini+ | 有线 USB 产品名称；接收器模式显示 `ATK 8K Dongle` | 用户实测：有线模式正常识别鼠标型号、显示电量和充电状态；接收器模式正常显示电量和充电状态，但界面显示接收器名称，尚未识别为 A9 Mini+。 |
| 罗技 LIGHTSPEED 鼠标／键盘 | HID++ 接收器与配对设备身份 | 动态读取设备名称、类型和电量功能；休眠时清除旧百分比。 |

### 其他电脑的用户实测记录

2026-09-24，用户报告已在其他电脑实测 **Logitech G PRO X2 SUPERSTRIKE** 和 **Logitech G502 X LIGHTSPEED**；两款鼠标的电量显示、充电状态和休眠处理均正常。具体连接方式、读取的百分比数值和拔插接收器结果未提供。详细记录见本机的 `WIDGET-VERIFICATION.md`。

用户还报告 **ATK A9 Mini+**：有线模式下型号、电量和充电状态正常；接收器模式下界面显示 `ATK 8K Dongle`，电量和充电状态正常。接收器模式的鼠标型号识别仍未确认。

## 构建与安装

普通用户可直接运行 `publish\GearPulse-1.2.0-Setup.exe`，无需安装 .NET SDK。安装向导默认勾选登录时启动；可取消，之后仍可从托盘切换。安装包只为当前 Windows 用户安装，并在卸载时询问是否清除设置与日志。制作安装包的步骤见 [安装包说明](installer/README.md)。

在 Windows x64 上安装 .NET 10 SDK，然后在仓库目录运行：

```powershell
pwsh -NoProfile -File .\Publish-GearPulse.ps1
pwsh -NoProfile -File .\Install-BatteryWidget.ps1
```

V1.2 增加两套图标、三档尺寸、独立的背景与内容透明度，以及显示器和角落选择。当前发布目录是 `publish\win-x64\GearPulse.exe`；安装脚本将当前用户的 `GearPulse` 登录任务指向该 EXE。V1.3.2 独立候选程序在 `publish\1.3.2-candidate\GearPulse.exe`，不会自动替换当前发布程序；重新构建可运行 `pwsh -NoProfile -File .\Publish-GearPulse.ps1 -OutputPath .\publish\1.3.2-candidate`。详细操作见 [WIDGET-README.md](WIDGET-README.md)。

## 验证

```powershell
dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release
pwsh -NoProfile -File .\Test-BlackSharkProtocol.ps1
```

离线测试不发送 HID 命令。可用 `dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release -- --atk-diagnostics` 查看 ATK 接收器 ID、接口规格、身份码、查询状态和电量；只查询已验证的 17 字节接口，不输出设备路径或序列号。V1.3.2 已通过 136 项离线检查。实机验证仍需检查电量、休眠、拔插接收器、充电状态、普通窗口遮挡和 Explorer 重启。状态不明时不沿用旧电量，也不把未知电量当作 0%。

ATK 系列发现范围是能够从 Windows USB 设备信息确认品牌的 ATK／VXE／VGN 设备，以及已收录产品 ID 的鼠标；蓝牙不在范围内。共用 Compx 芯片的其他品牌不会仅凭厂商 ID 被误认。可识别的接收器显示中性名称，不采用接收器 ID 或带型号的接收器描述来判断配对鼠标；无法从描述区分接收器与有线鼠标的设备仍需实机核对。17 字节鼠标协议匹配时才查询电量；其他协议的型号（包括 ATK Zero）目前显示未知电量。A9 Mini+ 接收器模式已由用户确认能显示电量和充电状态，但仍显示 `ATK 8K Dongle`；无线身份需取得有效应答后才能标为 A9 Mini+，不能借用 F1 的身份映射推测。已收录的鼠标 ID 参考 [Mouse Tray 的协议与型号表](https://github.com/Fan4Metal/mouse_tray/tree/master/mouse_tray/drivers/chipset)。

罗技支持限于能通过接收器的 HID++ 2.0 接口确认型号、名称、类型与真实电量百分比的无线设备；仅有电压或粗略档位的设备不会显示。BlackShark 协议参考 [OpenRazer PR #2862](https://github.com/openrazer/openrazer/pull/2862)，罗技 HID++ 功能参考 [Logitech 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)。本项目采用 [GPL-2.0-or-later](LICENSE)；本机日志、截图和原始 HID 报文不纳入仓库。
