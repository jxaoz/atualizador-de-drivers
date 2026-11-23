#define AppName "Drivers License"
#define AppPublisher "jxaoz"
#define AppUrl "https://github.com/jxaoz/atualizador-de-drivers"
#define AppGuid "0D63EBCF-AF52-4712-BEBA-2CF2378E4C17"
#define PortableExeName "DriversLicense.exe"
#define InstalledExeName "AtualizadorDeDrivers.exe"
#define LegacyAppName "Atualizador de Drivers"

#ifndef AppBinary
  #define AppBinary SourcePath + PortableExeName
#endif
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(AppBinary, "ProductVersion")
#endif

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases/latest
DefaultDirName={userpf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousGroup=no
PrivilegesRequired=lowest
MinVersion=10.0
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#InstalledExeName}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
OutputBaseFilename=Drivers-License-{#AppVersion}-Setup
OutputDir=.
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
CloseApplicationsFilter={#InstalledExeName}
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "{#InstalledExeName}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#InstalledExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#InstalledExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#InstalledExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent unchecked

[CustomMessages]
ptbr.FrameworkRequired={#AppName} requer o .NET Framework 4.8 ou superior. Instale o .NET Framework e execute este instalador novamente.
en.FrameworkRequired={#AppName} requires .NET Framework 4.8 or later. Install .NET Framework and then run this installer again.

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';

var
  PreviousInstallDirectory: String;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if Result then
    RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', PreviousInstallDirectory)
  else
    SuppressibleMsgBox(CustomMessage('FrameworkRequired'), mbError, MB_OK, IDOK);
end;

function RemoveOwnedShortcut(const LinkPath, AppPath: String): Boolean;
var
  Shell, Shortcut: Variant;
  TargetPath: String;
begin
  Result := False;
  if not FileExists(LinkPath) then
    Exit;

  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(LinkPath);
    TargetPath := Shortcut.TargetPath;
    if CompareText(TargetPath, AppPath) = 0 then
      Result := DeleteFile(LinkPath);
  except
    Log('Unable to inspect legacy shortcut: ' + LinkPath);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppPath, OldGroup: String;
begin
  if (CurStep <> ssPostInstall) or (PreviousInstallDirectory = '') then
    Exit;
  if CompareText(AddBackslash(PreviousInstallDirectory), AddBackslash(ExpandConstant('{app}'))) <> 0 then
    Exit;

  AppPath := ExpandConstant('{app}\{#InstalledExeName}');
  OldGroup := ExpandConstant('{userprograms}\{#LegacyAppName}');
  if RemoveOwnedShortcut(OldGroup + '\{#LegacyAppName}.lnk', AppPath) then
    RemoveDir(OldGroup);
  RemoveOwnedShortcut(ExpandConstant('{userdesktop}\{#LegacyAppName}.lnk'), AppPath);
end;
