#ifndef AppVersion
 #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
 #define PublishDir "..\artifacts\StartFlow-" + AppVersion + "-win-x64"
#endif
[Setup]
AppId={{D5819456-F3A5-450B-A8F1-8E964830FBA3}
AppName=StartFlow
AppVersion={#AppVersion}
AppVerName=StartFlow {#AppVersion}
DefaultDirName={localappdata}\Programs\StartFlow
DefaultGroupName=StartFlow
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=StartFlow-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\StartFlow.App\Assets\StartFlow.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\StartFlow.exe
UninstallDisplayName=StartFlow
AppMutex=Local\StartFlowInstallerGuard
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,Data\*"
[Icons]
Name: "{autoprograms}\StartFlow"; Filename: "{app}\StartFlow.exe"
Name: "{autoprograms}\StartFlow - Guide"; Filename: "{app}\docs\用户与制作教程.md"
[Run]
Filename: "{app}\StartFlow.exe"; Description: "Open StartFlow"; Flags: nowait postinstall skipifsilent
[Code]
var RepairUpgradeStartup: Boolean;
function InitializeSetup(): Boolean;
var OldDir, StartupCommand: String;
begin
  RepairUpgradeStartup := False;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5819456-F3A5-450B-A8F1-8E964830FBA3}_is1', 'Inno Setup: App Path', OldDir) then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartFlow', StartupCommand) then
      RepairUpgradeStartup := CompareText(StartupCommand, '"' + AddBackslash(OldDir) + 'StartFlow.exe" --login') = 0;
  Result := True;
end;
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and RepairUpgradeStartup then
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartFlow', '"' + ExpandConstant('{app}\StartFlow.exe') + '" --login');
end;
function InitializeUninstall(): Boolean;
begin
  if CheckForMutexes('Local\StartFlowInstallerGuard') then begin
    MsgBox('Please exit StartFlow from its tray menu before uninstalling. 请先从托盘退出启序。', mbInformation, MB_OK);
    Result := False;
    Exit;
  end;
  Result := SuppressibleMsgBox('Uninstall StartFlow? Its own local configuration will be deleted by default.' + #13#10 +
    'To back up first: Cancel, open StartFlow > Settings & data > Export.' + #13#10 +
    'Your referenced apps, files, folders, scripts and exported backups will NOT be deleted.' + #13#10 + #13#10 +
    '卸载将默认删除启序自己的配置。要先备份，请取消并打开启序 → 设置与数据 → 导出。' + #13#10 +
    '不会删除引用的软件、脚本、文档、文件夹或导出的备份。', mbConfirmation, MB_OKCANCEL or MB_DEFBUTTON2, IDOK) = IDOK;
end;
procedure DeleteOwnPattern(Pattern: String);
var Found: TFindRec; Folder: String;
begin
  Folder := ExpandConstant('{localappdata}\StartFlow\');
  if FindFirst(Folder + Pattern, Found) then begin
    try
      repeat
        if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then DeleteFile(Folder + Found.Name);
      until not FindNext(Found);
    finally FindClose(Found); end;
  end;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartFlow', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\StartFlow.exe') + '" --login') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StartFlow');
    DeleteOwnPattern('config.json');
    DeleteOwnPattern('config.json.bak');
    DeleteOwnPattern('config.json.before-import-*');
    DeleteOwnPattern('config.json.recovery-*');
    DeleteOwnPattern('config.json.*.tmp');
    RemoveDir(ExpandConstant('{localappdata}\StartFlow'));
  end;
end;
