; Inno Setup script of RCynQ: a per-user installation without administrator rights.
; The installation is portable in the sense of the application (the rcynq.portable marker sits in its root): the
; settings (config) and all data (keys, database) stay in the folders "config" and "data" inside the installation, so
; nothing is written elsewhere and an update never touches the personal data of the node.
; Build it with Scripts/App/build-installer.ps1, which passes the version, the source folder, the pictures and the
; license pages.

#ifndef RCynQVersion
  #define RCynQVersion "0.1.0"
#endif
; The page of the project (PackageProjectUrl of Directory.Build.props): the link of the program in the list of
; installed apps.
#ifndef RCynQUrl
  #define RCynQUrl "https://depthrel.github.io/RCynQ/"
#endif
#ifndef RCynQSource
  #define RCynQSource SourcePath + "..\..\artifacts\publish\win-x64"
#endif
#ifndef RCynQOutputDir
  #define RCynQOutputDir SourcePath + "..\..\artifacts\publish"
#endif
#ifndef RCynQIcon
  #define RCynQIcon SourcePath + "..\..\Sources\Apps\RCynQ.UI\Assets\rcynq.ico"
#endif
; The pictures of the wizard, drawn by Scripts/Assets/generate-installer-art.ps1 (build-installer.ps1 runs it).
#ifndef InstallerArt
  #define InstallerArt SourcePath + "..\..\artifacts\installer-art"
#endif
; The license page of every language: a short introduction and the English text of the GPL, written by
; Write-InstallerLicenses (Scripts/Shared/package-notes.ps1, run by the build script).
#ifndef InstallerLicense
  #define InstallerLicense SourcePath + "..\..\artifacts\installer-license"
#endif

[Setup]
AppId={{D3F6A1B4-5C2E-4B7A-9E18-6A0C3B7D2F41}
AppName=RCynQ
AppVersion={#RCynQVersion}
AppPublisher=RCynQ
AppPublisherURL={#RCynQUrl}
AppSupportURL={#RCynQUrl}
AppUpdatesURL={#RCynQUrl}
AppCopyright=GPL-3.0-or-later
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
; The logo instead of the stock box: a dark panel in the look of the application on the welcome page, the logo alone in
; the header of the others. Without the pictures (the script was not run) the stock ones are used.
#if FileExists(InstallerArt + "\rcynq-wizard.bmp")
WizardImageFile={#InstallerArt}\rcynq-wizard.bmp
WizardSmallImageFile={#InstallerArt}\rcynq-wizard-small.bmp
#endif
OutputDir={#RCynQOutputDir}
OutputBaseFilename=RCynQ-{#RCynQVersion}-setup
#ifdef RCynQSign
; The installer and the uninstaller are signed by the command build-installer.ps1 passes as /Srcynqsign.
SignTool=rcynqsign
SignedUninstaller=yes
#endif
CloseApplications=yes
CloseApplicationsFilter=RCynQ.exe,RCynQ.UI.exe,RCynQ.Service.exe,rcynq.exe
; The wizard opens with a greeting, not with a question.
DisableWelcomePage=no
; The language is asked first, with the language of Windows preselected; a Windows in a language the wizard does not
; speak gets English, the first in [Languages].
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage

[Languages]
; The first language is the one offered by default. A new language is one more line here and one more block in
; [Messages] and [CustomMessages].
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.en.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.de.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.es.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.fr.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.pt.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "{#InstallerLicense}\LICENSE.ru.txt"

[Messages]
; The greeting of the first page; [name] and [name/ver] are filled in by Inno Setup.
english.WelcomeLabel1=Welcome to the [name] setup
english.WelcomeLabel2=RCynQ (Remote Code sYNchronization, Quick) delivers your uncommitted changes to a remote machine and brings its branches back: directly, or through a Relay that only passes encrypted traffic.%n%nThis wizard installs [name/ver] on your computer.
german.WelcomeLabel1=Willkommen bei der Installation von [name]
german.WelcomeLabel2=RCynQ (Remote Code sYNchronization, Quick) bringt Ihre nicht committeten Änderungen auf einen entfernten Rechner und holt dessen Branches zurück: direkt oder über ein Relay, das nur verschlüsselten Verkehr durchlässt.%n%nDieser Assistent installiert [name/ver] auf Ihrem Computer.
spanish.WelcomeLabel1=Te damos la bienvenida a la instalación de [name]
spanish.WelcomeLabel2=RCynQ (Remote Code sYNchronization, Quick) lleva tus cambios sin commit a una máquina remota y trae de vuelta sus ramas: directamente o a través de un Relay que solo deja pasar tráfico cifrado.%n%nEste asistente instalará [name/ver] en tu equipo.
french.WelcomeLabel1=Bienvenue dans l'installation de [name]
french.WelcomeLabel2=RCynQ (Remote Code sYNchronization, Quick) envoie vos modifications non commitées sur une machine distante et en rapporte les branches : directement, ou par un Relay qui ne laisse passer que du trafic chiffré.%n%nCet assistant va installer [name/ver] sur votre ordinateur.
portuguese.WelcomeLabel1=Bem-vindo à instalação do [name]
portuguese.WelcomeLabel2=O RCynQ (Remote Code sYNchronization, Quick) leva as suas alterações sem commit para uma máquina remota e traz de volta os ramos dela: diretamente ou através de um Relay que só deixa passar tráfego cifrado.%n%nEste assistente vai instalar o [name/ver] no seu computador.
russian.WelcomeLabel1=Добро пожаловать в установку [name]
russian.WelcomeLabel2=RCynQ (Remote Code sYNchronization, Quick) доставляет ваши незакоммиченные изменения на удалённую машину и приносит её ветки обратно: напрямую или через узел Relay, который лишь пропускает зашифрованный трафик.%n%nМастер установит [name/ver] на ваш компьютер.

[CustomMessages]
; Everything the wizard says beyond the standard messages of Inno Setup.
english.DesktopIcon=Desktop shortcut
english.AdditionalTasks=Additional tasks:
english.LaunchRCynQ=Start RCynQ
english.DeleteDataPrompt=Also delete the personal data of RCynQ (settings, keys, database) from the folders "config" and "data"?
english.DeleteDataHint=Choose "No" if you are going to install RCynQ again.
german.DesktopIcon=Verknüpfung auf dem Desktop
german.AdditionalTasks=Zusätzlich:
german.LaunchRCynQ=RCynQ starten
german.DeleteDataPrompt=Auch die persönlichen Daten von RCynQ (Einstellungen, Schlüssel, Datenbank) aus den Ordnern „config“ und „data“ löschen?
german.DeleteDataHint=Wählen Sie „Nein“, wenn Sie RCynQ erneut installieren wollen.
spanish.DesktopIcon=Acceso directo en el escritorio
spanish.AdditionalTasks=Adicional:
spanish.LaunchRCynQ=Iniciar RCynQ
spanish.DeleteDataPrompt=¿Borrar también los datos personales de RCynQ (ajustes, claves, base de datos) de las carpetas «config» y «data»?
spanish.DeleteDataHint=Elige «No» si vas a volver a instalar RCynQ.
french.DesktopIcon=Raccourci sur le bureau
french.AdditionalTasks=Options supplémentaires :
french.LaunchRCynQ=Lancer RCynQ
french.DeleteDataPrompt=Supprimer aussi les données personnelles de RCynQ (réglages, clés, base de données) des dossiers « config » et « data » ?
french.DeleteDataHint=Choisissez « Non » si vous comptez réinstaller RCynQ.
portuguese.DesktopIcon=Atalho no ambiente de trabalho
portuguese.AdditionalTasks=Adicional:
portuguese.LaunchRCynQ=Iniciar o RCynQ
portuguese.DeleteDataPrompt=Eliminar também os dados pessoais do RCynQ (definições, chaves, base de dados) das pastas «config» e «data»?
portuguese.DeleteDataHint=Escolha «Não» se vai voltar a instalar o RCynQ.
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
{ On screens scaled above 100% the boxes of the lists are drawn larger while their distance from the edge stays the
  same, so the left side of a box is cut off. The distance is scaled with the rest of the wizard. }
procedure InitializeWizard();
begin
  WizardForm.TasksList.Offset := ScaleX(8);
  WizardForm.RunList.Offset := ScaleX(8);
end;

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
