; Inno Setup script for DM Enhancer.
; Build:  ISCC.exe /DAppVersion=1.0.0 installer\DmEnhancer.iss
; Expects the app to be built into bin\app first (dotnet build -c Release -o bin\app).
; Installs per user (no admin prompt) into %LOCALAPPDATA%\Programs\DM Enhancer.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "DM Enhancer"
#define AppExe "DmEnhancer.exe"

[Setup]
AppId={{A68CA695-7B77-47DC-BE04-D1DACFC149A0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ymankh
AppPublisherURL=https://github.com/ymankh/dm-enhancer
AppSupportURL=https://github.com/ymankh/dm-enhancer/issues
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=DmEnhancer-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running copy before upgrading or uninstalling.
CloseApplications=force
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\app\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" checkbox uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DmEnhancer"; ValueData: """{app}\{#AppExe}"""; Tasks: startup
; Always remove it on uninstall, even if it was turned on from the app's settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DmEnhancer"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"
