# GearPulse｜外设脉动

Windows 桌面电量小组件，显示 Razer BlackShark V2 Pro、两只 ATK 鼠标，以及通过 HID++ LIGHTSPEED 接收器连接并报告真实百分比的罗技鼠标和键盘。正式程序是 .NET 10 WPF 独立 EXE，不需要 PowerShell 常驻运行；PowerShell 仅用于构建、安装和诊断。

| 设备 | 识别方式 | 行为 |
|---|---|---|
| BlackShark V2 Pro | `1532:0555` | 电量、充电状态和接收器状态。 |
| ATK F1 V3 Ultimate+ | `373B:1278` | 接收器拔出后隐藏；休眠时清除旧百分比。 |
| ATK A9 Plus NK | `373B:10C9` | 与 F1 独立显示，可同时使用。 |
| 罗技 LIGHTSPEED 鼠标／键盘 | HID++ 接收器与配对设备身份 | 动态读取设备名称、类型和电量功能；休眠时清除旧百分比。 |

## 构建与安装

在 Windows x64 上安装 .NET 10 SDK，然后在仓库目录运行：

```powershell
pwsh -NoProfile -File .\Publish-GearPulse.ps1
pwsh -NoProfile -File .\Install-BatteryWidget.ps1
```

发布结果在 `publish\win-x64\GearPulse.exe`，是包含运行时的独立程序。安装脚本将当前用户的 `GearPulse` 登录任务指向该 EXE；运行中的旧小组件会停止。首次切换前应直接运行 EXE，检查实际桌面显示。详细操作见 [WIDGET-README.md](WIDGET-README.md)。

## 验证

```powershell
dotnet run --project .\tests\GearPulse.Smoke\GearPulse.Smoke.csproj -c Release
pwsh -NoProfile -File .\Test-BlackSharkProtocol.ps1
```

离线测试不发送 HID 命令。实机验证仍需检查电量、休眠、拔插接收器、充电状态、普通窗口遮挡和 Explorer 重启。状态不明时不沿用旧电量，也不把未知电量当作 0%。

罗技支持限于能通过接收器的 HID++ 2.0 接口确认型号、名称、类型与真实电量百分比的无线设备；仅有电压或粗略档位的设备不会显示。多数耳机使用不同的协议，目前不会显示。BlackShark 协议参考 [OpenRazer PR #2862](https://github.com/openrazer/openrazer/pull/2862)，罗技 HID++ 功能参考 [Logitech 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)。本项目采用 [GPL-2.0-or-later](LICENSE)；本机日志、截图和原始 HID 报文不纳入仓库。
