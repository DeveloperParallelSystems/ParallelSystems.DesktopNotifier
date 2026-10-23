#define MyAppName "Parallel Systems Desktop Notifier"
#define MyAppVersion "1.3.0"
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
AppMutex=Local\ParallelSystems.DesktopNotifier.SingleInstance

[Files]
Source: "..\artifacts\dependencies\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
Source: "..\Assets\logo-mark.ico"; DestDir: "{app}"; DestName: "Product.ico"; Flags: ignoreversion
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Parallel Systems Desktop Notifier"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Product.ico"; IconIndex: 0

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Parallel Systems Desktop Notifier"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start Parallel Systems Desktop Notifier"; Flags: nowait postinstall skipifsilent

[Code]
function ValidRuntimeVersion(Root: Integer; Key: String): Boolean;
var Version: String;
begin
  Result := RegQueryStringValue(Root, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function HasWebView2Runtime: Boolean;
var Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := ValidRuntimeVersion(HKCU, Key) or ValidRuntimeVersion(HKLM32, Key);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if not HasWebView2Runtime then
  begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'Unable to start Microsoft WebView2 Runtime setup. Install the Evergreen Runtime and retry.'
    else if not HasWebView2Runtime then
      Result := 'Microsoft WebView2 Runtime could not be installed. Check your internet connection or ask your administrator to install the Evergreen Runtime, then retry.';
  end;
end;
