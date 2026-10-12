; Per-user Penelope setup. The host is one self-contained exe.
; Plugins stay as files beside it so a new tool can be added without rebuilding the host.

#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef PublishDir
#define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{B7E4C1A8-2F63-4A15-9D0E-6C8B5A4F1E27}
AppName=Penelope
AppVersion={#AppVersion}
AppPublisher=Penelope
DefaultDirName={localappdata}\Programs\Penelope
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=PenelopeSetup
UninstallDisplayName=Penelope
CloseApplications=yes
AppMutex=Local\Penelope.Host
WizardStyle=modern
SetupIconFile=..\src\Penelope.Host\Assets\penelope.ico
UninstallDisplayIcon={app}\Penelope.Host.exe
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: desktopicon; Description: "Add a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\COPYRIGHT"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\web\src\design\fonts\OFL.txt"; DestDir: "{app}"; DestName: "FiraCode-OFL.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Penelope"; Filename: "{app}\Penelope.Host.exe"; IconFilename: "{app}\Penelope.Host.exe"
Name: "{userdesktop}\Penelope"; Filename: "{app}\Penelope.Host.exe"; IconFilename: "{app}\Penelope.Host.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Penelope.Host.exe"; Description: "Open Penelope"; Flags: nowait postinstall skipifsilent
