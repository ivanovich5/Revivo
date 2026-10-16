#ifndef SourceRoot
#define SourceRoot "..\stage"
#endif
#ifndef BuildAppId
#define BuildAppId "{{7B2FF733-93C2-4AC6-A177-110B40398C50}"
#endif
#ifndef BuildName
#define BuildName "Revivo"
#endif
#ifndef OutputName
#define OutputName "Revivo-0.9.17-beta-setup"
#endif
[Setup]
AppId={#BuildAppId}
AppName={#BuildName}
AppVersion=0.9.17
AppPublisher=Proyecto Revivo
DefaultDirName={localappdata}\Programs\Revivo
DefaultGroupName=Revivo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\release
OutputBaseFilename={#OutputName}
SetupIconFile=..\src\Rescate.Desktop\Brand\revivo.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\app\Revivo.exe
CloseApplications=yes
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Files]
Source: "{#SourceRoot}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\app-copia-092\*"; DestDir: "{app}\app-copia-092"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\app-reutilizar\*"; DestDir: "{app}\app-reutilizar"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\engines\*"; DestDir: "{app}\engines"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\python\*"; DestDir: "{app}\python"; Excludes: "__pycache__\*"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\scripts\*.py"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\*.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\platform-notices\*"; DestDir: "{app}\platform-notices"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\third-party-source\*"; DestDir: "{app}\third-party-source"; Flags: ignoreversion recursesubdirs
Source: "{#SourceRoot}\GUIA-BETA.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\{#BuildName}"; Filename: "{app}\app\Revivo.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#BuildName}"; Filename: "{app}\app\Revivo.exe"; WorkingDir: "{app}"
[Run]
Filename: "{app}\app\Revivo.exe"; Description: "Abrir Revivo"; Flags: nowait postinstall skipifsilent