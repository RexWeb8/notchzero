; Inno Setup script for NotchZero (built by GitHub Actions — see build.yml).
; Installs per user (no admin rights needed), so it works on locked-down office PCs.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F1C2D4E-8B7A-4C1E-9A55-2B0D8E1F7A33}
AppName=NotchZero
AppVersion={#AppVersion}
AppVerName=NotchZero {#AppVersion}
AppPublisher=NotchZero
DefaultDirName={localappdata}\Programs\NotchZero
DefaultGroupName=NotchZero
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=NotchZero-Setup
SetupIconFile=NotchZero.ico
UninstallDisplayIcon={app}\NotchZero.exe
UninstallDisplayName=NotchZero
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start NotchZero when Windows starts"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "publish\NotchZero.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\NotchZero"; Filename: "{app}\NotchZero.exe"
Name: "{autodesktop}\NotchZero"; Filename: "{app}\NotchZero.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NotchZero"; ValueData: """{app}\NotchZero.exe"""; Flags: uninsdeletevalue; Tasks: startup
; Also clean up the value if it was turned on later from inside the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NotchZero"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\NotchZero.exe"; Description: "Launch NotchZero"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im NotchZero.exe"; Flags: runhidden; RunOnceId: "StopNotchZero"
