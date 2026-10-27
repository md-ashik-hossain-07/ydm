; Inno Setup Script for YDM (Your Download Manager)
#define MyAppName "YDM"
#define MyAppFullName "Your Download Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "YDM Open Source Contributors"
#define MyAppURL "https://github.com/md-ashik-hossain-07/ydm"
#define MyAppExeName "YDM.UI.exe"

[Setup]
AppId={{5A1D2B9F-83C4-4B2E-9D11-47E53B02C481}
AppName={#MyAppFullName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppFullName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=ydm_setup_{#MyAppVersion}_win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start YDM automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\ui\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\host\com.ydm.downloader.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppFullName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppFullName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppFullName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppFullName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Registry]
; Chromium Native Messaging Host registration for Chrome and Edge (current user)
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.ydm.downloader"; ValueType: string; ValueData: "{app}\com.ydm.downloader.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.ydm.downloader"; ValueType: string; ValueData: "{app}\com.ydm.downloader.json"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppFullName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
