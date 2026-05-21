[Setup]
AppName=Satisfactory Session Tracker
AppVersion=1.0.0
DefaultDirName={autopf}\SatisfactorySessionTracker
DefaultGroupName=Satisfactory Session Tracker
OutputDir=dist
OutputBaseFilename=SFT-Tracker-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\main\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Satisfactory Session Tracker"; Filename: "{app}\main.exe"
Name: "{autodesktop}\Satisfactory Session Tracker"; Filename: "{app}\main.exe"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "sft"; ValueType: string; ValueName: ""; ValueData: "URL:Satisfactory Tracker Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "sft"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCR; Subkey: "sft\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\main.exe,0"
Root: HKCR; Subkey: "sft\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\main.exe"" ""%1"""

[Run]
Filename: "{app}\main.exe"; Description: "{cm:LaunchProgram,Satisfactory Session Tracker}"; Flags: nowait postinstall skipifsilent
