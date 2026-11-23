#define AppName "Atualizador de Drivers"
#define AppPublisher "jxaoz"
#define AppUrl "https://github.com/jxaoz/atualizador-de-drivers"
#define AppExeName "AtualizadorDeDrivers.exe"

#ifndef AppBinary
  #define AppBinary SourcePath + AppExeName
#endif
#ifndef AppVersion
  #define AppVersion GetStringFileInfo(AppBinary, "ProductVersion")
#endif

[Setup]
AppId={{0D63EBCF-AF52-4712-BEBA-2CF2378E4C17}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases/latest
DefaultDirName={userpf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
OutputBaseFilename=Atualizador-de-Drivers-{#AppVersion}-Setup
OutputDir=.
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent unchecked

[CustomMessages]
ptbr.FrameworkRequired={#AppName} requer o .NET Framework 4.8 ou superior. Instale o .NET Framework e execute este instalador novamente.
en.FrameworkRequired={#AppName} requires .NET Framework 4.8 or later. Install .NET Framework and then run this installer again.

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('FrameworkRequired'), mbError, MB_OK, IDOK);
end;
