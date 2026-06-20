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
PrivilegesRequired=admin
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

[UninstallDelete]
; Program files (always deleted)
Type: files; Name: "{app}\.SmartGuard.initialized"
Type: filesandordirs; Name: "{app}\bin"
Type: filesandordirs; Name: "{app}\lib"
; User data (optional, based on user choice)
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
        (Pos('SmartGuard.Tray.exe', Output) > 0) or
        (Pos('SmartGuard.Engine.exe', Output) > 0) or
        (Pos('SmartGuard.LogViewer.exe', Output) > 0) or
        (Pos('SmartGuard.Settings.exe', Output) > 0);
    end;
  end;
  DeleteFile(OutputFile);
end;

procedure StopSmartGuardProcesses();
var
  ResultCode: Integer;
  WaitAttempts: Integer;
begin
  { Step 0: Stop and disable scheduled tasks FIRST in a single shell call.
    If the engine is running under the task scheduler (e.g. as SYSTEM),
    taskkill alone may fail or the task may immediately restart it via
    RestartOnFailure. Ending and disabling the tasks prevents revival. }
  Exec(ExpandConstant('{cmd}'),
    '/C schtasks /End /TN "SmartGuard Guardian" /F >nul 2>&1 & ' +
    'schtasks /End /TN "SmartGuard Tray" /F >nul 2>&1 & ' +
    'schtasks /Change /TN "SmartGuard Guardian" /Disable >nul 2>&1 & ' +
    'schtasks /Change /TN "SmartGuard Tray" /Disable >nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Step 1: Kill all SmartGuard processes in one taskkill call. }
  Exec(ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM SmartGuard.Tray.exe /IM SmartGuard.Engine.exe /IM SmartGuard.LogViewer.exe /IM SmartGuard.Settings.exe /T',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Step 2: Wait for processes to actually exit (max 3s) with fast polling. }
  for WaitAttempts := 1 to 60 do
  begin
    if not SmartGuardProcessesStillRunning() then
      break;
    Sleep(50);
  end;

  { Step 3: Delete scheduled tasks after processes are stopped (single shell call). }
  Exec(ExpandConstant('{cmd}'),
    '/C schtasks /Delete /TN "SmartGuard Guardian" /F >nul 2>&1 & ' +
    'schtasks /Delete /TN "SmartGuard Tray" /F >nul 2>&1',
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
  DeleteUserData := False;
  SmartGuardStopCompleted := False;
end;

procedure InitializeUninstallProgressForm();
var
  Choice: Integer;
begin
  { This runs after the standard "Are you sure you want to uninstall?" dialog,
    so the user-data choice appears in the correct order. }
  if UninstallSilent then
    Exit;

  Choice := MsgBox(
    '程序文件与计划任务将被移除。' + #13#10 + #13#10 +
    '是否同时删除配置与日志文件？' + #13#10 +
    '点击“是”删除这些数据；点击“否”保留配置与日志。',
    mbConfirmation,
    MB_YESNO);

  DeleteUserData := (Choice = IDYES);
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

procedure DeleteUserDataFiles;
var
  FindRec: TFindRec;
  LogPattern: String;
begin
  DeleteFile(ExpandConstant('{app}\SmartGuard.config.json'));
  DeleteFile(ExpandConstant('{app}\SmartGuard.log'));
  DeleteFile(ExpandConstant('{app}\SmartGuard.startup.log'));
  DeleteFile(ExpandConstant('{app}\SmartGuard.status.json'));

  { Also remove rotated log backups such as SmartGuard.log.20260619.bak }
  LogPattern := ExpandConstant('{app}\SmartGuard.log.*.bak');
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
    { If the user chose to delete user data and the files are still present,
      stop processes one last time and delete them. This covers cases where
      the engine was restarted between usUninstall and usPostUninstall. }
    if DeleteUserData and
       (FileExists(ExpandConstant('{app}\SmartGuard.config.json')) or
        FileExists(ExpandConstant('{app}\SmartGuard.log')) or
        FileExists(ExpandConstant('{app}\SmartGuard.startup.log')) or
        FileExists(ExpandConstant('{app}\SmartGuard.status.json'))) then
    begin
      TryStopSmartGuardProcesses();
      DeleteUserDataFiles;
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
