#define AppName "WinOptimizer"
#define AppVersion "0.1.0"
#define AppExeName "WinOptimizer.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{59E2D177-A767-4308-B30D-95AA015CFDA8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WinOptimizer contributors
DefaultDirName={localappdata}\Programs\WinOptimizer
DefaultGroupName=WinOptimizer
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=WinOptimizer-Setup-{#AppVersion}-win-x64
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinOptimizer"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\WinOptimizer"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch WinOptimizer"; Flags: nowait postinstall skipifsilent
