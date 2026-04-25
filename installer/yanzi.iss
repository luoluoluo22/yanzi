#define AppName "Yanzi"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef PublishDir
#define PublishDir "..\.artifacts\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\.artifacts\installer"
#endif

[Setup]
AppId={{1F2FE5FB-1986-4D2A-AF2C-37A1E52750A6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Yanzi
DefaultDirName={autopf}\Yanzi
DefaultGroupName=Yanzi
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=YanziSetup-{#AppVersion}
SetupIconFile=..\src\OpenQuickHost\yanzi.ico
UninstallDisplayIcon={app}\Yanzi.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Yanzi"; Filename: "{app}\Yanzi.exe"
Name: "{autodesktop}\Yanzi"; Filename: "{app}\Yanzi.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Yanzi.exe"; Description: "启动 Yanzi"; Flags: nowait postinstall skipifsilent
