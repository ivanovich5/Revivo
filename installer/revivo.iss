#ifndef SourceRoot
#define SourceRoot "..\stage"
#endif
#ifndef BuildAppId
#define BuildAppId "{{7B2FF733-93C2-4AC6-A177-110B40398C50}"
#endif
#ifndef BuildName
#define BuildName "REVIVO-USB"
#endif
#ifndef OutputName
#define OutputName "REVIVO-USB-1.0.1-setup"
#endif
#ifndef BuildVersion
#define BuildVersion "1.0.1"
#endif
#ifndef BundleSources
#define BundleSources 1
#endif
[Setup]
AppId={#BuildAppId}
AppName={#BuildName}
AppVersion={#BuildVersion}
AppPublisher=Proyecto Revivo
AppPublisherURL=https://revivo.digital/
AppSupportURL=https://revivo.digital/#feedback
LicenseFile={#SourceRoot}\LICENSE.txt
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
#if BundleSources
Source: "{#SourceRoot}\third-party-source\*"; DestDir: "{app}\third-party-source"; Flags: ignoreversion recursesubdirs
#endif
Source: "{#SourceRoot}\GUIA-BETA.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\{#BuildName}"; Filename: "{app}\app\Revivo.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#BuildName}"; Filename: "{app}\app\Revivo.exe"; WorkingDir: "{app}"
[Run]
Filename: "{app}\app\Revivo.exe"; Description: "Abrir REVIVO-USB"; Flags: nowait postinstall skipifsilent
