; Inno Setup 7 script for Opsis Media Player Pro (also compiles with 6.x).
; Build with build.ps1 (it publishes the self-contained app into dist\app first).
; Silent install: OpsisMediaPlayerPro-Setup.exe /VERYSILENT
#define MyAppName "Opsis Media Player Pro"
#define MyAppVersion "1.2.0"
#define MyAppExe "OpsisMediaPlayerPro.exe"
#ifndef DistDir
  #define DistDir "..\dist\app"
#endif

[Setup]
AppId={{B7E1D2C4-5A36-4F0B-9C58-2E4D7F61A903}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Opsis Team
SetupIconFile=..\app.ico
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\Output
OutputBaseFilename=OpsisMediaPlayerPro-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "greek"; MessagesFile: "Greek.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
english.OpenWithMenu=Add "Open with Opsis Media Player Pro" to the right-click menu of video and audio files
greek.DesktopShortcut=Δημιουργία συντόμευσης στην επιφάνεια εργασίας
greek.OpenWithMenu=Προσθήκη «Άνοιγμα με Opsis Media Player Pro» στο μενού δεξιού κλικ των αρχείων βίντεο και ήχου

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked
Name: "openwith"; Description: "{cm:OpenWithMenu}"; Flags: unchecked

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Only a per-user "Open with" entry for the common extensions: it does NOT take over the default player, and it is removed on uninstall.
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExe}"" ""%1"""; Tasks: openwith; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithList\{#MyAppExe}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithList\{#MyAppExe}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithList\{#MyAppExe}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithList\{#MyAppExe}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithList\{#MyAppExe}"; Flags: uninsdeletekey; Tasks: openwith

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{#MyAppName}"; Flags: nowait postinstall skipifsilent
