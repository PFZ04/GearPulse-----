# GearPulse｜外设脉动

当前发布版本：**V1.2**（产品版本 `1.2.0`，Windows 文件版本 `1.2.0.0`）。

Windows 桌面电量小组件，显示 Razer BlackShark V2 Pro、通过 USB 接收器或有线 USB 连接的 ATK／VXE／VGN 外设，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。正式程序是 .NET 10 WPF 独立 EXE，不需要 PowerShell 常驻运行；PowerShell 仅用于构建、安装和诊断。

| 设备 | 识别方式 | 行为 |
|---|---|---|
| BlackShark V2 Pro | `1532:0555` | 电量、充电状态和接收器状态。 |
| ATK／VXE／VGN 鼠标、键盘、耳机 | USB 设备容器、厂商信息或已核实的产品 ID | 自动发现并去重；有已验证电量协议时读取百分比，否则显示“电量暂不可用”。 |
| ATK F1 V3 Ultimate+、A9 Plus NK | 接收器 `373B:1278`、`373B:10C9` 与鼠标身份应答 | 保留已实测的在线、型号和电量查询；接收器 ID 不直接决定鼠标型号，休眠时清除旧百分比。 |
| 罗技 LIGHTSPEED 鼠标／键盘 | HID++ 接收器与配对设备身份 | 动态读取设备名称、类型和电量功能；休眠时清除旧百分比。 |

## 构建与安装

普通用户可直接运行 `publish\GearPulse-1.2.0-Setup.exe`，无需安装 .NET SDK。安装向导默认勾选登录时启动；可取消，之后仍可从托盘切换。安装包只为当前 Windows 用户安装，并在卸载时询问是否清除设置与日志。制作安装包的步骤见 [安装包说明](installer/README.md)。

在 Windows x64 上安装 .NET 10 SDK，然后在仓库目录运行：

```powershell
pwsh -NoProfile -File .\Publish-GearPulse.ps1
pwsh -NoProfile -File .\Install-BatteryWidget.ps1
```

V1.2 增加两套图标、三档尺寸、独立的背景与内容透明度，以及显示器和角落选择。发布结果在 `publish\win-x64\GearPulse.exe`，是包含运行时的独立程序。安装脚本将当前用户的 `GearPulse` 登录任务指向该 EXE。替换正在运行的程序前，可用 `pwsh -NoProfile -File .\Publish-GearPulse.ps1 -OutputPath .\publish\v1.2-candidate` 先构建候选包并完成验证。详细操作见 [WIDGET-README.md](WIDGET-README.md)。

## 验证

```powershell
dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release
pwsh -NoProfile -File .\Test-BlackSharkProtocol.ps1
```

离线测试不发送 HID 命令。可用 `dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release -- --atk-integration` 做 ATK 设备只读诊断。实机验证仍需检查电量、休眠、拔插接收器、充电状态、普通窗口遮挡和 Explorer 重启。状态不明时不沿用旧电量，也不把未知电量当作 0%。

ATK 系列发现范围是能够从 Windows USB 设备信息确认品牌的 ATK／VXE／VGN 设备，以及已核实产品 ID 的鼠标；蓝牙不在范围内。共用 Compx 芯片的其他品牌不会仅凭厂商 ID 被误认。17 字节鼠标协议匹配时才查询电量；其他协议的型号（包括 ATK Zero）目前显示未知电量。型号未知时显示接收器名称；若系统只提供通用名称，则显示厂商／产品 ID。已核实的鼠标 ID 参考 [Mouse Tray 的协议与型号表](https://github.com/Fan4Metal/mouse_tray/tree/master/mouse_tray/drivers/chipset)。

罗技支持限于能通过接收器的 HID++ 2.0 接口确认型号、名称、类型与真实电量百分比的无线设备；仅有电压或粗略档位的设备不会显示。BlackShark 协议参考 [OpenRazer PR #2862](https://github.com/openrazer/openrazer/pull/2862)，罗技 HID++ 功能参考 [Logitech 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)。本项目采用 [GPL-2.0-or-later](LICENSE)；本机日志、截图和原始 HID 报文不纳入仓库。
