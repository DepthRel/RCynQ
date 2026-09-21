; Inno Setup script of RCynQ: a per-user installation without administrator rights.
; The installation is portable in the sense of the application (the rcynq.portable marker sits in its root): the
; settings (config) and all data (keys, database) stay in the folders "config" and "data" inside the installation, so
; nothing is written elsewhere and an update never touches the personal data of the node.
; Build it with Scripts/build-installer.ps1, which passes the version and the source folder.

#ifndef RCynQVersion
  #define RCynQVersion "1.0.0"
#endif
#ifndef RCynQSource
  #define RCynQSource "..\artifacts\publish\win-x64"
#endif
#ifndef RCynQOutputDir
  #define RCynQOutputDir "..\artifacts\publish"
#endif
#ifndef RCynQIcon
  #define RCynQIcon "..\Sources\Apps\RCynQ.UI\Assets\rcynq.ico"
#endif

[Setup]
AppId={{D3F6A1B4-5C2E-4B7A-9E18-6A0C3B7D2F41}
AppName=RCynQ
AppVersion={#RCynQVersion}
AppPublisher=RCynQ
VersionInfoVersion={#RCynQVersion}
DefaultDirName={localappdata}\Programs\RCynQ
DefaultGroupName=RCynQ
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#RCynQIcon}
UninstallDisplayIcon={app}\RCynQ.exe
OutputDir={#RCynQOutputDir}
OutputBaseFilename=RCynQ-{#RCynQVersion}-setup
#ifdef RCynQSign
; The installer and the uninstaller are signed by the command build-installer.ps1 passes as /Srcynqsign.
SignTool=rcynqsign
SignedUninstaller=yes
#endif
CloseApplications=yes
CloseApplicationsFilter=RCynQ.exe,RCynQ.UI.exe,RCynQ.Service.exe,rcynq.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.DesktopIcon=Desktop shortcut
english.AdditionalTasks=Additional tasks:
english.LaunchRCynQ=Start RCynQ
english.DeleteDataPrompt=Also delete the personal data of RCynQ (settings, keys, database) from the folders "config" and "data"?
english.DeleteDataHint=Choose "No" if you are going to install RCynQ again.
russian.DesktopIcon=Значок на рабочем столе
russian.AdditionalTasks=Дополнительно:
russian.LaunchRCynQ=Запустить RCynQ
russian.DeleteDataPrompt=Удалить и личные данные RCynQ (настройки, ключи, базу данных) из папок «config» и «data»?
russian.DeleteDataHint=Нажмите «Нет», если собираетесь поставить RCynQ снова.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
; Everything from the published package except the settings and the data of a node that was tried out from it.
Source: "{#RCynQSource}\*"; DestDir: "{app}"; Excludes: "config\*,config,data\*,data"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\RCynQ"; Filename: "{app}\RCynQ.exe"; IconFilename: "{app}\RCynQ.exe"
Name: "{userdesktop}\RCynQ"; Filename: "{app}\RCynQ.exe"; IconFilename: "{app}\RCynQ.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RCynQ.exe"; Description: "{cm:LaunchRCynQ}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir: string;
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    ConfigDir := ExpandConstant('{app}\config');
    DataDir := ExpandConstant('{app}\data');
    if DirExists(ConfigDir) or DirExists(DataDir) then
    begin
      if MsgBox(ExpandConstant('{cm:DeleteDataPrompt}')
        + #13#10 + #13#10 + ConfigDir + #13#10 + DataDir + #13#10 + #13#10
        + ExpandConstant('{cm:DeleteDataHint}'), mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(ConfigDir, True, True, True);
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
