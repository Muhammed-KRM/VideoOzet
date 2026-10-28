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
Password=Hoca2026!
Encryption=yes
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

[Code]
var
  InstallCompletedSuccessfully: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  TicketPath: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    TicketPath := ExpandConstant('{app}\.install_ticket');
    SaveStringToFile(TicketPath, 'VZ-INSTALL-AUTH-2026-9F8E7D6C', False);
    Exec(ExpandConstant('{app}\VideoOzet.API.exe'), '--seal-hardware', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else if CurStep = ssDone then
  begin
    InstallCompletedSuccessfully := True;
  end;
end;

procedure DeinitializeSetup();
var
  ResultCode: Integer;
  SetupExePath: String;
begin
  if InstallCompletedSuccessfully then
  begin
    SetupExePath := ExpandConstant('{srcexe}');
    if Pos('d:\hoca\videoozet\dagitimpaketi\', LowerCase(SetupExePath)) = 0 then
    begin
      Exec(ExpandConstant('{cmd}'), '/c ping 127.0.0.1 -n 3 > nul & del /f /q "' + SetupExePath + '"', '', SW_HIDE, ewNoWait, ResultCode);
    end;
  end;
end;
