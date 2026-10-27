[Setup]
AppId={{8F9E4A21-7C3B-4D1E-9A55-2B8C6E4D1F09}
AppName=VideoÖzet AI
AppVersion=1.0.0
AppPublisher=VideoÖzet AI
DefaultDirName={autopf}\VideoOzetAI
DefaultGroupName=VideoÖzet AI
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=DagitimPaketi
OutputBaseFilename=VideoOzet_Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "DagitimPaketi\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\VideoÖzet AI"; Filename: "{app}\VideoOzet.API.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\VideoÖzet AI"; Filename: "{app}\VideoOzet.API.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\VideoOzet.API.exe"; Description: "{cm:LaunchProgram,VideoÖzet AI}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
