#define MyAppName "Parallel Systems Desktop Notifier"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Parallel Systems"
#define MyAppExeName "ParallelSystems.DesktopNotifier.exe"

[Setup]
AppId={{A4B6E781-4DC0-4CC9-9DAF-3FCB9F4943BD}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Parallel Systems\Desktop Notifier
DefaultGroupName=Parallel Systems
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\artifacts\installer
OutputBaseFilename=ParallelSystems.DesktopNotifier-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\Assets\logo-mark.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ParallelSystems.DesktopNotifier

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Parallel Systems Desktop Notifier"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Parallel Systems Desktop Notifier"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start Parallel Systems Desktop Notifier"; Flags: nowait postinstall skipifsilent
