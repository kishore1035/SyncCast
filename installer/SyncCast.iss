; Inno Setup Script for SyncCast (Optional Per-User Windows Installer)
; To compile: Install Inno Setup 6 (https://jrsoftware.org/isdl.php) and run:
; iscc.exe installer\SyncCast.iss

#define MyAppName "SyncCast"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SyncCast Project"
#define MyAppExeName "SyncCast.exe"

[Setup]
AppId={{D1A3F5B8-7E92-4C61-B920-569A72A4E08F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=SyncCast-{#MyAppVersion}-Setup
OutputDir=..\artifacts
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\SyncCast\Resources\SyncCast.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\SyncCast-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent
