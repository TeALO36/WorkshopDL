; ============================================================
;  WorkshopDL 2.0.6 — Inno Setup script
;  Modern, friendly installer (per-user, no admin prompt).
;  Ships the updated supported-games list (Golf With Your
;  Friends included) + the new WorkshopDL Launcher GUI.
; ============================================================
#define MyAppName "WorkshopDL"
#define MyAppVersion "2.0.6"
#define MyAppPublisher "TeALO36"
#define MyAppURL "https://github.com/TeALO36/WorkshopDL"
#define MyAppExeName "WorkshopDL Launcher.exe"

[Setup]
AppId={{7B2F1F5B-3C1A-4E5A-9D4E-2C6A8B0F1D21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Setup
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=WorkshopDL.{#MyAppVersion}_installer
SetupIconFile=..\assets\workshopdl.ico
UninstallDisplayIcon={app}\WorkshopDL.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallFilesDir={app}\uninstall
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
[CustomMessages]
en.LaunchNow=&Launch {#MyAppName} now
fr.LaunchNow=Lancer {#MyAppName} maintenant
en.DesktopLauncherTask=Create a desktop shortcut for the Launcher (recommended)
fr.DesktopLauncherTask=Créer un raccourci bureau pour le Launcher (recommandé)
en.LauncherFileIcon=Desktop shortcut for the Launcher
fr.LauncherFileIcon=Raccourci bureau du Launcher
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "desktoplauncher"; Description: "{cm:DesktopLauncherTask}"; GroupDescription: "{cm:AdditionalIcons}"
[Files]
; --- main application (portable payload, prepared in staging) ---
Source: "..\staging\WorkshopDL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\staging\WorkshopDL.dat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\staging\WorkshopDL.ini"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "..\staging\WorkshopDL Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\staging\Modules\*"; DestDir: "{app}\Modules"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\staging\README.txt"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\WorkshopDL"; Filename: "{app}\WorkshopDL.exe"
Name: "{group}\WorkshopDL Launcher"; Filename: "{app}\WorkshopDL Launcher.exe"
Name: "{group}\Uninstall WorkshopDL"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WorkshopDL Launcher"; Filename: "{app}\WorkshopDL Launcher.exe"; Tasks: desktoplauncher
Name: "{autodesktop}\WorkshopDL"; Filename: "{app}\WorkshopDL.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\WorkshopDL Launcher.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: filesandordirs; Name: "{app}\steamcmd"
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Existing: String;
begin
  Existing := ExpandConstant('{app}\WorkshopDL.exe');
  if FileExists(Existing) then
    MsgBox('WorkshopDL is already installed in:'#13#10 + Existing + #13#10#13#10 +
      'Your settings (WorkshopDL.ini) and downloaded lists will be kept.',
      mbInformation, MB_OK);
  Result := '';
end;
