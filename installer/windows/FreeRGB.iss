; Inno Setup script for the FreeRGB Windows installer.
; Build:  iscc /DAppVersion=0.1.0 /DSourceDir=<publish folder> /DOutputDir=<out folder> installer\windows\FreeRGB.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{6C1D2E1A-4B7F-4E5A-9A57-3F2B8C0D9E41}
AppName=FreeRGB
AppVersion={#AppVersion}
AppVerName=FreeRGB {#AppVersion}
AppPublisher=FreeRGB contributors
AppPublisherURL=https://github.com/VVVULTURE/FreeRGB
AppSupportURL=https://github.com/VVVULTURE/FreeRGB/issues
; Per-user install by default: no administrator rights needed. The user may choose "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\FreeRGB
DefaultGroupName=FreeRGB
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=FreeRGB-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\FreeRGB.App\Assets\icon.ico
UninstallDisplayIcon={app}\FreeRGB.exe
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start FreeRGB when I sign in (minimised to the tray)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\FreeRGB"; Filename: "{app}\FreeRGB.exe"
Name: "{autodesktop}\FreeRGB"; Filename: "{app}\FreeRGB.exe"; Tasks: desktopicon

[Registry]
; Same per-user Run entry the app's own "Start at sign-in" setting writes, so the two stay consistent.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FreeRGB"; \
  ValueData: """{app}\FreeRGB.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\FreeRGB.exe"; Description: "Launch FreeRGB"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings and profiles in %APPDATA%\FreeRGB are deliberately kept. If you used "Set up hardware support", the elevated
; scheduled task FreeRGB-OpenRGB and the PawnIO driver are separate and must be removed by an administrator
; (see SECURITY.md).
Type: filesandordirs; Name: "{app}"
