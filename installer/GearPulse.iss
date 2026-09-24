#define AppVersion "1.2.0"
#ifndef TestBuild
  #define TestBuild "0"
#endif
#if TestBuild == "1"
  #ifndef TestInstallDir
    #error TestInstallDir must be supplied by Build-Installer.ps1
  #endif
  #ifndef TestUserDataDir
    #error TestUserDataDir must be supplied by Build-Installer.ps1
  #endif
  #define InstallName "GearPulse Installer Test"
  #define InstallerAppId "GearPulse.InstallerTest.9DE209C1"
  #define TaskName "GearPulse.InstallerTest"
  #define InstallDir TestInstallDir
  #define UserDataDir TestUserDataDir
  #define OutputName "GearPulse-1.2.0-Test-Setup"
#else
  #define InstallName "GearPulse"
  #define InstallerAppId "GearPulse.1F6B89C9"
  #define TaskName "GearPulse"
  #define InstallDir "{localappdata}\Programs\GearPulse"
  #define UserDataDir "{localappdata}\GearPulse"
  #define OutputName "GearPulse-1.2.0-Setup"
#endif

[Setup]
AppId={#InstallerAppId}
AppName={#InstallName}
AppVersion={#AppVersion}
AppVerName={#InstallName} {#AppVersion}
DefaultDirName={#InstallDir}
DefaultGroupName={#InstallName}
OutputDir=..\publish
OutputBaseFilename={#OutputName}
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
UsePreviousTasks=yes
CloseApplications=no
VersionInfoVersion=1.2.0.0
VersionInfoDescription=GearPulse v1.2 installer
UninstallDisplayIcon={app}\GearPulse.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "zhtw"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[CustomMessages]
english.StartupTask=Start GearPulse when I sign in
zhcn.StartupTask=登录 Windows 时启动 GearPulse
zhtw.StartupTask=登入 Windows 時啟動 GearPulse
english.RemoveData=Remove saved settings and logs? Choose No to keep them for a later reinstall.
zhcn.RemoveData=是否删除已保存的设置和日志？选择“否”可供以后重装使用。
zhtw.RemoveData=是否刪除已儲存的設定和記錄？選擇「否」可供日後重新安裝使用。
english.TaskError=Could not configure the GearPulse login task.
zhcn.TaskError=无法配置 GearPulse 登录任务。
zhtw.TaskError=無法設定 GearPulse 登入工作。

[Tasks]
Name: "autostart"; Description: "{cm:StartupTask}"

[Files]
Source: "..\publish\win-x64\GearPulse.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "InstallerTask.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "InstallerTask.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\GearPulse"; Filename: "{app}\GearPulse.exe"

#if TestBuild == "0"
[Run]
Filename: "{app}\GearPulse.exe"; Description: "{cm:LaunchProgram,GearPulse}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
var
  RemoveUserData: Boolean;

function TaskHelperPath(Uninstalling: Boolean): String;
begin
  if Uninstalling then
    Result := ExpandConstant('{app}\InstallerTask.ps1')
  else begin
    ExtractTemporaryFile('InstallerTask.ps1');
    Result := ExpandConstant('{tmp}\InstallerTask.ps1');
  end;
end;

function RunTaskHelper(Mode: String; Enabled: Boolean; Uninstalling: Boolean; var ErrorText: String): Boolean;
var
  ScriptPath, ErrorPath, Params: String;
  ErrorBytes: AnsiString;
  ExitCode: Integer;
begin
  ScriptPath := TaskHelperPath(Uninstalling);
  ErrorPath := ExpandConstant('{tmp}\GearPulse-installer-error.txt');
  if FileExists(ErrorPath) then DeleteFile(ErrorPath);
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(ScriptPath) +
    ' -Mode ' + Mode + ' -TaskName ' + AddQuotes('{#TaskName}') +
    ' -ExePath ' + AddQuotes(ExpandConstant('{app}\GearPulse.exe')) +
    ' -ErrorFile ' + AddQuotes(ErrorPath);
  if not Enabled then Params := Params + ' -DisableAutostart';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then begin
    ErrorText := CustomMessage('TaskError');
    if FileExists(ErrorPath) then begin
      if LoadStringFromFile(ErrorPath, ErrorBytes) then
        ErrorText := ErrorText + #13#10 + String(ErrorBytes);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RunTaskHelper('Preflight', True, False, Result) then Exit;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorText: String;
begin
  if CurStep = ssPostInstall then
    if not RunTaskHelper('Install', WizardIsTaskSelected('autostart'), False, ErrorText) then
      RaiseException(ErrorText);
end;

function InitializeUninstall: Boolean;
var
  Answer: Integer;
  ErrorText: String;
begin
  RemoveUserData := Pos('/DELETEUSERDATA', Uppercase(GetCmdTail)) > 0;
  if not UninstallSilent and not RemoveUserData then begin
    Answer := MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNOCANCEL);
    if Answer = IDCANCEL then begin Result := False; Exit; end;
    RemoveUserData := Answer = IDYES;
  end;
  Result := RunTaskHelper('Uninstall', False, True, ErrorText);
  if not Result and not UninstallSilent then MsgBox(ErrorText, mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    DelTree(ExpandConstant('{#UserDataDir}'), True, True, True);
end;
