; SmartGuard Inno Setup script (Phase 5 — P5A)
; Build: installer\Build-Installer.ps1

#ifndef StagingDir
  #define StagingDir "staging"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef RuntimeInstallerFile
  #define RuntimeInstallerFile "windowsdesktop-runtime-8.0.18-win-x64.exe"
#endif

#define MyAppName "SmartGuard"
#define MyAppPublisher "rainiva"
#define MyAppURL "https://github.com/rainiva/SmartGuard"
#define MyAppExeName "SmartGuard.Tray.exe"
#define MySetupIcon "..\lib\SmartGuard.ico"

[Setup]
AppId={{8F3C2A1B-4D5E-6F70-8A9B-0C1D2E3F4A5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
DisableDirPage=auto
LicenseFile={#StagingDir}\license_zh-CN.txt
OutputDir=..\dist
OutputBaseFilename=SmartGuard-Setup-{#MyAppVersion}-x64
SetupIconFile={#MySetupIcon}
Compression=lzma2
SolidCompression=no
WizardStyle=modern
PrivilegesRequired=lowest
CloseApplications=no
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\lib\SmartGuard.ico

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式（托盘）"; GroupDescription: "附加图标:"; Flags: unchecked
Name: "launchtray"; Description: "安装完成后启动托盘"; GroupDescription: "安装后:"; Flags: checkedonce

[Files]
Source: "{#StagingDir}\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace
Source: "{#StagingDir}\lib\SmartGuard.ico"; DestDir: "{app}\lib"; Flags: ignoreversion
Source: "{#StagingDir}\lib\SmartGuard.Settings.xaml"; DestDir: "{app}\lib"; Flags: ignoreversion
Source: "{#StagingDir}\license_zh-CN.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingDir}\redist\{#RuntimeInstallerFile}"; DestDir: "{tmp}"; DestName: "{#RuntimeInstallerFile}"; Flags: deleteafterinstall; Check: ShouldInstallDotNet

[Icons]
Name: "{group}\{#MyAppName} 托盘"; Filename: "{app}\bin\{#MyAppExeName}"; Parameters: "--root ""{app}"""
Name: "{group}\{#MyAppName} 设置"; Filename: "{app}\bin\SmartGuard.Settings.exe"; Parameters: "--root ""{app}"""
Name: "{group}\{#MyAppName} 日志"; Filename: "{app}\bin\SmartGuard.LogViewer.exe"; Parameters: "--root ""{app}"""
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\bin\{#MyAppExeName}"; Parameters: "--root ""{app}"""; Tasks: desktopicon

[Run]
Filename: "{tmp}\{#RuntimeInstallerFile}"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 .NET 8 桌面运行时..."; Check: ShouldInstallDotNet; Flags: waituntilterminated
Filename: "{app}\bin\SmartGuard.Engine.exe"; Parameters: "--root ""{app}"" --install --skip-publish"; StatusMsg: "正在注册计划任务..."; Flags: waituntilterminated
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""SmartGuard Guardian"""; StatusMsg: "正在启动核心服务..."; Flags: runhidden waituntilterminated
Filename: "{app}\bin\{#MyAppExeName}"; Parameters: "--root ""{app}"""; Description: "启动 {#MyAppName} 托盘"; Flags: nowait postinstall skipifsilent; Tasks: launchtray

[UninstallRun]
Filename: "{app}\bin\SmartGuard.Engine.exe"; Parameters: "--root ""{app}"" --uninstall"; Flags: waituntilterminated

[UninstallDelete]
Type: files; Name: "{app}\SmartGuard.config.json"; Check: ShouldDeleteUserData
Type: files; Name: "{app}\SmartGuard.log"; Check: ShouldDeleteUserData
Type: files; Name: "{app}\SmartGuard.startup.log"; Check: ShouldDeleteUserData
Type: files; Name: "{app}\SmartGuard.status.json"; Check: ShouldDeleteUserData

[Code]
var
  DeleteUserData: Boolean;
  SmartGuardStopCompleted: Boolean;

function ShouldDeleteUserData: Boolean;
begin
  Result := DeleteUserData;
end;

procedure StopSmartGuardProcesses();
var
  ResultCode: Integer;
begin
  Exec(
    ExpandConstant('{cmd}'),
    '/C schtasks /End /TN "SmartGuard Guardian" 2>nul &' +
    ' schtasks /End /TN "SmartGuard Tray" 2>nul &' +
    ' taskkill /F /IM SmartGuard.Tray.exe /T 2>nul &' +
    ' taskkill /F /IM SmartGuard.Engine.exe /T 2>nul &' +
    ' taskkill /F /IM SmartGuard.LogViewer.exe /T 2>nul &' +
    ' taskkill /F /IM SmartGuard.Settings.exe /T 2>nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function SmartGuardProcessesStillRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(
    ExpandConstant('{cmd}'),
    '/C tasklist /NH 2>nul | findstr /I /C:"SmartGuard.Tray.exe" /C:"SmartGuard.Engine.exe" /C:"SmartGuard.LogViewer.exe" /C:"SmartGuard.Settings.exe" >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function TryStopSmartGuardProcesses(): Boolean;
begin
  if not SmartGuardProcessesStillRunning() then
  begin
    Result := True;
    Exit;
  end;

  StopSmartGuardProcesses();
  Result := not SmartGuardProcessesStillRunning();
end;

function EnsureSmartGuardStopped(): Boolean;
begin
  if SmartGuardStopCompleted then
  begin
    Result := not SmartGuardProcessesStillRunning();
    Exit;
  end;

  Result := TryStopSmartGuardProcesses();
  if Result then
    SmartGuardStopCompleted := True;
end;

function GetExistingSmartGuardInstallPath(): String;
var
  Path: string;
begin
  Result := '';
  if RegQueryStringValue(
    HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2A1B-4D5E-6F70-8A9B-0C1D2E3F4A5B}_is1',
    'InstallLocation', Path) then
    Result := RemoveBackslash(Path);
end;

#ifndef uninstaller
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  if SmartGuardStopCompleted and not SmartGuardProcessesStillRunning() then
    Exit;
  if not SmartGuardProcessesStillRunning() then
  begin
    SmartGuardStopCompleted := True;
    Exit;
  end;
  WizardForm.StatusLabel.Caption := '正在关闭 SmartGuard 进程…';
  WizardForm.Repaint;
  if not EnsureSmartGuardStopped() then
    Result :=
      '无法自动关闭正在运行的 SmartGuard（托盘/引擎/日志/设置）。' + #13#10 +
      '请在任务管理器中结束 SmartGuard*.exe 后重试。';
end;
#endif

#ifdef uninstaller
var
  UninstallOptionsPage: TNewNotebookPage;
  UninstallProceedButton: TNewButton;
  KeepUserDataRadio: TNewRadioButton;
  DeleteUserDataRadio: TNewRadioButton;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteUserData := False;
  StopSmartGuardProcesses();
end;

procedure InitializeUninstallProgressForm();
var
  HintLabel: TNewStaticText;
  OriginalPageNameLabel: string;
  OriginalPageDescriptionLabel: string;
  OriginalCancelButtonEnabled: Boolean;
  OriginalCancelButtonModalResult: Integer;
  Ctrl: TWinControl;
begin
  if UninstallSilent then
    Exit;

  DeleteUserData := False;

  Ctrl := UninstallProgressForm.CancelButton;
  UninstallProceedButton := TNewButton.Create(UninstallProgressForm);
  UninstallProceedButton.Parent := UninstallProgressForm;
  UninstallProceedButton.Left := Ctrl.Left - Ctrl.Width - ScaleX(10);
  UninstallProceedButton.Top := Ctrl.Top;
  UninstallProceedButton.Width := Ctrl.Width;
  UninstallProceedButton.Height := Ctrl.Height;
  UninstallProceedButton.TabOrder := Ctrl.TabOrder;
  UninstallProceedButton.Caption := SetupMessage(msgButtonNext);
  UninstallProceedButton.ModalResult := mrOk;
  UninstallProgressForm.CancelButton.TabOrder := UninstallProceedButton.TabOrder + 1;

  UninstallOptionsPage := TNewNotebookPage.Create(UninstallProgressForm);
  UninstallOptionsPage.Notebook := UninstallProgressForm.InnerNotebook;
  UninstallOptionsPage.Parent := UninstallProgressForm.InnerNotebook;
  UninstallOptionsPage.Align := alClient;
  UninstallProgressForm.InnerNotebook.ActivePage := UninstallOptionsPage;

  HintLabel := TNewStaticText.Create(UninstallProgressForm);
  HintLabel.Parent := UninstallOptionsPage;
  HintLabel.Top := ScaleY(8);
  HintLabel.Left := ScaleX(8);
  HintLabel.Width := UninstallOptionsPage.ClientWidth - ScaleX(16);
  HintLabel.Height := ScaleY(44);
  HintLabel.AutoSize := False;
  HintLabel.WordWrap := True;
  HintLabel.ShowAccelChar := False;
  HintLabel.Caption := '程序文件与计划任务将被移除。请选择如何处理配置与日志：';

  KeepUserDataRadio := TNewRadioButton.Create(UninstallOptionsPage);
  KeepUserDataRadio.Parent := UninstallOptionsPage;
  KeepUserDataRadio.Left := ScaleX(8);
  KeepUserDataRadio.Top := ScaleY(60);
  KeepUserDataRadio.Width := UninstallOptionsPage.ClientWidth - ScaleX(16);
  KeepUserDataRadio.Caption := '保留配置与日志（推荐）';
  KeepUserDataRadio.Checked := True;

  DeleteUserDataRadio := TNewRadioButton.Create(UninstallOptionsPage);
  DeleteUserDataRadio.Parent := UninstallOptionsPage;
  DeleteUserDataRadio.Left := ScaleX(8);
  DeleteUserDataRadio.Top := ScaleY(88);
  DeleteUserDataRadio.Width := UninstallOptionsPage.ClientWidth - ScaleX(16);
  DeleteUserDataRadio.Caption := '删除配置与日志（不可恢复）';

  OriginalPageNameLabel := UninstallProgressForm.PageNameLabel.Caption;
  OriginalPageDescriptionLabel := UninstallProgressForm.PageDescriptionLabel.Caption;
  OriginalCancelButtonEnabled := UninstallProgressForm.CancelButton.Enabled;
  OriginalCancelButtonModalResult := UninstallProgressForm.CancelButton.ModalResult;

  UninstallProgressForm.PageNameLabel.Caption := '用户数据';
  UninstallProgressForm.PageDescriptionLabel.Caption :=
    '默认保留配置与日志；仅在选择删除时才会移除这些文件。';
  UninstallProgressForm.CancelButton.Enabled := True;
  UninstallProgressForm.CancelButton.ModalResult := mrCancel;

  if UninstallProgressForm.ShowModal = mrCancel then
    Abort;

  DeleteUserData := DeleteUserDataRadio.Checked;

  UninstallProceedButton.Visible := False;
  UninstallProgressForm.PageNameLabel.Caption := OriginalPageNameLabel;
  UninstallProgressForm.PageDescriptionLabel.Caption := OriginalPageDescriptionLabel;
  UninstallProgressForm.CancelButton.Enabled := OriginalCancelButtonEnabled;
  UninstallProgressForm.CancelButton.ModalResult := OriginalCancelButtonModalResult;
  UninstallProgressForm.InnerNotebook.ActivePage := UninstallProgressForm.InstallingPage;
end;
#endif

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ExistingPath: String;
  SelectedPath: String;
begin
  Result := True;

#ifndef uninstaller
  if CurPageID = wpReady then
  begin
    if SmartGuardProcessesStillRunning() then
      StopSmartGuardProcesses();
    SmartGuardStopCompleted := not SmartGuardProcessesStillRunning();
  end
  else if CurPageID = wpSelectDir then
  begin
    ExistingPath := GetExistingSmartGuardInstallPath();
    SelectedPath := RemoveBackslash(WizardForm.DirEdit.Text);
    if (ExistingPath <> '') and (CompareText(ExistingPath, SelectedPath) <> 0) then
    begin
      MsgBox(
        '本机已安装 SmartGuard，仅允许保留一个实例。' + #13#10 +
        '当前安装位置：' + ExistingPath + #13#10 +
        '请卸载后重装到该目录，或继续使用原目录升级。',
        mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
#endif
end;

function InitializeSetup: Boolean;
begin
  SmartGuardStopCompleted := False;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopSmartGuardProcesses();
end;

function IsDesktopDotNet8Installed: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        exit;
      end;
  end;
end;

function ShouldInstallDotNet: Boolean;
begin
  Result := not IsDesktopDotNet8Installed;
end;
