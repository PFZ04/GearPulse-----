# GearPulse 安装包

`publish\GearPulse-1.2.0-Setup.exe` 是 Windows x64 的当前用户安装包；目标电脑无需 .NET SDK、PowerShell 7 或 Inno Setup。安装到 `%LOCALAPPDATA%\Programs\GearPulse`，创建开始菜单入口和卸载记录。安装向导支持简体中文、繁體中文和英文，默认启用登录时启动并在安装结束运行 GearPulse。取消登录启动时，安装程序会注册一个禁用的任务，因此托盘开关仍可使用。

`publish\GearPulse-1.3.5-Setup.exe` 是由独立的 1.3.5 候选 EXE 构建的新版安装包；构建过程不会运行安装程序，也不会替换本机的现有安装。

覆盖安装会把现有 GearPulse 登录任务指向安装目录，并保留 `%LOCALAPPDATA%\GearPulse` 中的设置。安装前若同名任务属于其他程序，安装会停止且不替换该任务。卸载时只移除指向本次安装目录的任务，并询问是否删除设置和日志；静默卸载默认保留。

## 构建

先用 `Publish-GearPulse.ps1` 生成 `publish\win-x64\GearPulse.exe`。安装 [Inno Setup 7](https://jrsoftware.org/isdl.php)，然后运行：

```powershell
pwsh -NoProfile -File .\Build-Installer.ps1 -IsccPath 'C:\Path\To\Inno Setup 7\ISCC.exe'
```

默认命令仍打包 `publish\win-x64\GearPulse.exe`。打包 1.3.5 候选版时运行：

```powershell
pwsh -NoProfile -File .\Build-Installer.ps1 -SourceExe .\publish\1.3.5-hide-unreadable-candidate\GearPulse.exe -ExpectedVersion 1.3.5
```

打包脚本会核对指定 EXE 的文件版本，并打印安装包路径、大小和 SHA-256。若构建工具位于 `publish\build-tools\InnoSetup7\ISCC.exe`，可省略 `-IsccPath`。安装包未签名；打包工具不随安装包分发。

`Build-Installer.ps1 -TestBuild` 会生成独立的测试安装包，使用 `GearPulse.InstallerTest` 任务名和仓库下 `publish\installer-test` 目录，且不会自动运行小组件。测试包不应作为正式发行包发送。
