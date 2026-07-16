; SmartGuard Inno Setup script (Phase 5 — P5A)
; Build: installer\Build-Installer.ps1

#ifndef StagingDir
  #define StagingDir "staging"
#endif
#ifndef MyAppVersion
  #define MyAppVersion Trim(FileRead(FileOpen(SourcePath + 'version.txt')))
#endif
#ifndef RuntimeInstallerFile
  #define RuntimeInstallerFile "windowsdesktop-runtime-8.0.18-win-x64.exe"
#endif

#include "generated\SmartGuard.Distribution.Constants.iss.inc"

#define MyAppName "SmartGuard"
#define MyAppPublisher "rainiva"
#define MyAppURL "https://github.com/rainiva/SmartGuard"
#define MySetupIcon "..\assets\branding\SmartGuard.ico"
#define ConfigFileName "SmartGuard.config.json"
#define StatusFileName "SmartGuard.status.json"
#define DefaultLogFileName "SmartGuard.log"
#define StartupLogFileName "SmartGuard.startup.log"
#define InitMarkerFileName ".SmartGuard.initialized"

[Setup]
AppId={{8F3C2A1B-4D5E-6F70-8A9B-0C1D2E3F4A5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=yes
DisableDirPage=auto
LicenseFile={#StagingDir}\license_zh-CN.txt
OutputDir=..\dist
OutputBaseFilename=SmartGuard-Setup-{#MyAppVersion}-x64
SetupIconFile={#MySetupIcon}
Compression=lzma2
SolidCompression=no
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=no
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\bin\{#TrayExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式（托盘）"; GroupDescription: "附加图标:"; Flags: unchecked
Name: "launchtray"; Description: "安装完成后启动托盘"; GroupDescription: "安装后:"; Flags: checkedonce

[Files]
#include "generated\SmartGuard.Distribution.Files.iss.inc"
Source: "{#StagingDir}\redist\{#RuntimeInstallerFile}"; DestDir: "{tmp}"; DestName: "{#RuntimeInstallerFile}"; Flags: deleteafterinstall; Check: ShouldInstallDotNet

[InstallDelete]
#include "generated\SmartGuard.Distribution.LegacyCleanup.iss.inc"

[Run]
#include "generated\SmartGuard.Distribution.Run.iss.inc"

[UninstallDelete]
; Program files (always deleted)
Type: files; Name: "{app}\{#InitMarkerFileName}"
Type: filesandordirs; Name: "{app}\bin"
#include "generated\SmartGuard.Distribution.LegacyCleanup.iss.inc"
; User config/status (optional)
Type: files; Name: "{app}\{#ConfigFileName}"; Check: ShouldDeleteUserConfig
Type: files; Name: "{app}\{#StatusFileName}"; Check: ShouldDeleteUserConfig
; Logs (optional, default keep)
Type: files; Name: "{app}\{#DefaultLogFileName}"; Check: ShouldDeleteUserLogs
Type: files; Name: "{app}\{#StartupLogFileName}"; Check: ShouldDeleteUserLogs

[Code]
var
  DeleteUserConfig: Boolean;
  DeleteUserLogs: Boolean;
  SmartGuardStopCompleted: Boolean;
  DeleteUserConfigCheckBox: TNewCheckBox;
  DeleteUserLogsCheckBox: TNewCheckBox;

function ShouldDeleteUserConfig: Boolean;
begin
  Result := DeleteUserConfig;
end;

function ShouldDeleteUserLogs: Boolean;
begin
  Result := DeleteUserLogs;
end;

function SmartGuardProcessesStillRunning(): Boolean;
var
  ResultCode: Integer;
  OutputFile: String;
  Output: AnsiString;
begin
  Result := False;
  OutputFile := ExpandConstant('{tmp}\sg_tasklist.txt');
  if Exec(ExpandConstant('{cmd}'),
    '/C tasklist /NH > "' + OutputFile + '" 2>nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    if LoadStringFromFile(OutputFile, Output) then
    begin
      Result :=
#include "generated\SmartGuard.Distribution.Processes.iss.inc"
    end;
  end;
  DeleteFile(OutputFile);
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
  if Result = '' then
    if RegQueryStringValue(
      HKLM,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2A1B-4D5E-6F70-8A9B-0C1D2E3F4A5B}_is1',
      'InstallLocation', Path) then
      Result := RemoveBackslash(Path);
end;

procedure StopSmartGuardProcesses();
var
  ResultCode: Integer;
  RootPath: String;
  EngineExe: String;
begin
  { Single stop implementation: delegate to EngineLifecycle via CLI. }
  RootPath := RemoveBackslash(ExpandConstant('{app}'));
  if RootPath = '' then
    RootPath := GetExistingSmartGuardInstallPath();
  if RootPath = '' then
    Exit;

  EngineExe := RootPath + '\bin\{#EngineExeName}';
  if not FileExists(EngineExe) then
    Exit;

  Exec(EngineExe,
    '--root "' + RootPath + '" --uninstall',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteUserConfig := False;
  DeleteUserLogs := False;
  SmartGuardStopCompleted := False;
end;

procedure InitializeUninstallProgressForm();
var
  Page: TNewNotebookPage;
  InfoLabel: TNewStaticText;
  PathLabel: TNewStaticText;
  PathEdit: TNewEdit;
  UninstallButton: TNewButton;
  PageNameLabel: string;
  PageDescriptionLabel: string;
  CancelButtonEnabled: Boolean;
  CancelButtonModalResult: Integer;
  ModalResult: Integer;
begin
  { After the standard "Are you sure?" dialog; silent keeps both flags False. }
  if UninstallSilent then
    Exit;

  PageNameLabel := UninstallProgressForm.PageNameLabel.Caption;
  PageDescriptionLabel := UninstallProgressForm.PageDescriptionLabel.Caption;
  CancelButtonEnabled := UninstallProgressForm.CancelButton.Enabled;
  CancelButtonModalResult := UninstallProgressForm.CancelButton.ModalResult;

  UninstallButton := TNewButton.Create(UninstallProgressForm);
  UninstallButton.Parent := UninstallProgressForm;
  UninstallButton.Left :=
    UninstallProgressForm.CancelButton.Left - UninstallProgressForm.CancelButton.Width - ScaleX(10);
  UninstallButton.Top := UninstallProgressForm.CancelButton.Top;
  UninstallButton.Width := UninstallProgressForm.CancelButton.Width;
  UninstallButton.Height := UninstallProgressForm.CancelButton.Height;
  UninstallButton.Caption := '卸载';
  UninstallButton.ModalResult := mrOK;
  UninstallProgressForm.CancelButton.Enabled := True;
  UninstallProgressForm.CancelButton.ModalResult := mrCancel;
  UninstallProgressForm.CancelButton.TabOrder := UninstallButton.TabOrder + 1;

  Page := TNewNotebookPage.Create(UninstallProgressForm);
  Page.Notebook := UninstallProgressForm.InnerNotebook;
  Page.Parent := UninstallProgressForm.InnerNotebook;
  Page.Align := alClient;
  UninstallProgressForm.InnerNotebook.ActivePage := Page;

  UninstallProgressForm.PageNameLabel.Caption := '卸载 SmartGuard';
  UninstallProgressForm.PageDescriptionLabel.Caption := '从本机移除 SmartGuard';

  InfoLabel := TNewStaticText.Create(UninstallProgressForm);
  InfoLabel.Parent := Page;
  InfoLabel.Left := UninstallProgressForm.StatusLabel.Left;
  InfoLabel.Top := UninstallProgressForm.StatusLabel.Top;
  InfoLabel.Width := UninstallProgressForm.StatusLabel.Width;
  InfoLabel.Height := ScaleY(36);
  InfoLabel.AutoSize := False;
  InfoLabel.WordWrap := True;
  InfoLabel.ShowAccelChar := False;
  InfoLabel.Caption := 'SmartGuard 将从下列文件夹卸载。点击「卸载」开始。';

  PathLabel := TNewStaticText.Create(UninstallProgressForm);
  PathLabel.Parent := Page;
  PathLabel.Left := InfoLabel.Left;
  PathLabel.Top := InfoLabel.Top + InfoLabel.Height + ScaleY(8);
  PathLabel.Width := InfoLabel.Width;
  PathLabel.AutoSize := True;
  PathLabel.Caption := '卸载位置：';

  PathEdit := TNewEdit.Create(UninstallProgressForm);
  PathEdit.Parent := Page;
  PathEdit.Left := PathLabel.Left;
  PathEdit.Top := PathLabel.Top + PathLabel.Height + ScaleY(4);
  PathEdit.Width := InfoLabel.Width;
  PathEdit.Text := ExpandConstant('{app}');
  PathEdit.ReadOnly := True;
  PathEdit.TabStop := False;

  DeleteUserConfigCheckBox := TNewCheckBox.Create(UninstallProgressForm);
  DeleteUserConfigCheckBox.Parent := Page;
  DeleteUserConfigCheckBox.Left := PathEdit.Left;
  DeleteUserConfigCheckBox.Top := PathEdit.Top + PathEdit.Height + ScaleY(16);
  DeleteUserConfigCheckBox.Width := InfoLabel.Width;
  DeleteUserConfigCheckBox.Caption := '删除用户数据（配置与状态）';
  DeleteUserConfigCheckBox.Checked := False;

  DeleteUserLogsCheckBox := TNewCheckBox.Create(UninstallProgressForm);
  DeleteUserLogsCheckBox.Parent := Page;
  DeleteUserLogsCheckBox.Left := DeleteUserConfigCheckBox.Left;
  DeleteUserLogsCheckBox.Top := DeleteUserConfigCheckBox.Top + DeleteUserConfigCheckBox.Height + ScaleY(8);
  DeleteUserLogsCheckBox.Width := InfoLabel.Width;
  DeleteUserLogsCheckBox.Caption := '删除日志文件';
  DeleteUserLogsCheckBox.Checked := False;

  ModalResult := UninstallProgressForm.ShowModal;
  if ModalResult = mrCancel then
    Abort;

  DeleteUserConfig := DeleteUserConfigCheckBox.Checked;
  DeleteUserLogs := DeleteUserLogsCheckBox.Checked;

  UninstallButton.Visible := False;
  UninstallProgressForm.PageNameLabel.Caption := PageNameLabel;
  UninstallProgressForm.PageDescriptionLabel.Caption := PageDescriptionLabel;
  UninstallProgressForm.CancelButton.Enabled := CancelButtonEnabled;
  UninstallProgressForm.CancelButton.ModalResult := CancelButtonModalResult;
  UninstallProgressForm.InnerNotebook.ActivePage := UninstallProgressForm.InstallingPage;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ExistingPath: String;
  SelectedPath: String;
begin
  Result := True;

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
end;

function InitializeSetup: Boolean;
begin
  SmartGuardStopCompleted := False;
  Result := True;
end;

procedure DeleteUserConfigFiles;
begin
  DeleteFile(ExpandConstant('{app}\{#ConfigFileName}'));
  DeleteFile(ExpandConstant('{app}\{#StatusFileName}'));
end;

procedure DeleteUserLogFiles;
var
  FindRec: TFindRec;
  LogPattern: String;
begin
  DeleteFile(ExpandConstant('{app}\{#DefaultLogFileName}'));
  DeleteFile(ExpandConstant('{app}\{#StartupLogFileName}'));

  { Also remove rotated log backups such as SmartGuard.log.20260619.bak }
  LogPattern := ExpandConstant('{app}\{#DefaultLogFileName}.*.bak');
  if FindFirst(LogPattern, FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY = 0 then
          DeleteFile(ExpandConstant('{app}\') + FindRec.Name);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Show the progress page before stopping processes so the uninstaller
      does not appear to hang on the initial click. }
    if not SmartGuardStopCompleted then
    begin
      UninstallProgressForm.StatusLabel.Caption := '正在关闭 SmartGuard 进程…';
      UninstallProgressForm.Repaint;
      EnsureSmartGuardStopped();
      UninstallProgressForm.StatusLabel.Caption := '正在删除文件…';
    end;
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    { Retry optional deletes if files remain (e.g. engine restarted mid-uninstall). }
    if (DeleteUserConfig and
        (FileExists(ExpandConstant('{app}\{#ConfigFileName}')) or
         FileExists(ExpandConstant('{app}\{#StatusFileName}')))) or
       (DeleteUserLogs and
        (FileExists(ExpandConstant('{app}\{#DefaultLogFileName}')) or
         FileExists(ExpandConstant('{app}\{#StartupLogFileName}')))) then
    begin
      TryStopSmartGuardProcesses();
      if DeleteUserConfig then
        DeleteUserConfigFiles;
      if DeleteUserLogs then
        DeleteUserLogFiles;
    end;

    { Remove program directories that may still contain files after [UninstallDelete] }
    DelTree(ExpandConstant('{app}\bin'), True, True, True);
    DelTree(ExpandConstant('{app}\lib'), True, True, True);
  end;
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
