; ============================================================
;  WorkshopDL 2.0.8 — Inno Setup script
;  - Ships a SINGLE application: "WorkshopDL Launcher.exe" (the old
;    WorkshopDL.exe window has been merged into it).
;  Modern, friendly installer (per-user, no admin prompt).
;  - Cleanly uninstalls any previous WorkshopDL version
;    (other AppId, e.g. 2.0.4) before installing: no stale
;    registry entry, files or shortcuts are left behind.
;  - Skips the "launch now" post-install entry when Setup is
;    running elevated through Inno's internal spawn server
;    (mode "all users"): that channel can fail with
;    "Internal error: CallSpawnServer: Unexpected response: $0"
;    and abort an otherwise finished installation.
;  Ships the updated supported-games list (Golf With Your
;  Friends included) + the WorkshopDL Launcher GUI.
; ============================================================
#define MyAppName "WorkshopDL"
#define MyAppVersion "2.0.8"
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
UninstallDisplayIcon={app}\WorkshopDL Launcher.exe
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
en.CleanupFailed=Some files from a previous WorkshopDL version could not be removed automatically. Please uninstall the old version from Windows Settings, then install again.
fr.CleanupFailed=Certains fichiers d'une ancienne version de WorkshopDL n'ont pas pu être supprimés automatiquement. Veuillez désinstaller l'ancienne version depuis les Réglages de Windows, puis réinstaller.
[Tasks]
Name: "desktoplauncher"; Description: "{cm:DesktopLauncherTask}"; GroupDescription: "{cm:AdditionalIcons}"
[Files]
; --- single application (portable payload, prepared in staging) ---
Source: "..\staging\WorkshopDL Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\staging\Modules\*"; DestDir: "{app}\Modules"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\staging\README.txt"; DestDir: "{app}"; Flags: ignoreversion
[InstallDelete]
; Legacy Clickteam build — removed so upgrades of the same AppId stay clean too.
Type: files; Name: "{app}\WorkshopDL.exe"
Type: files; Name: "{app}\WorkshopDL.dat"
Type: files; Name: "{app}\WorkshopDL.ini"
Type: files; Name: "{app}\Modules\*.mfx"
Type: files; Name: "{app}\Modules\mmfs2.dll"
Type: files; Name: "{app}\Modules\gmad.exe"
[Icons]
Name: "{group}\WorkshopDL"; Filename: "{app}\WorkshopDL Launcher.exe"
Name: "{group}\Uninstall WorkshopDL"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WorkshopDL"; Filename: "{app}\WorkshopDL Launcher.exe"; Tasks: desktoplauncher
[Run]
Filename: "{app}\WorkshopDL Launcher.exe"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent; Check: CanLaunchAfterInstall
[UninstallDelete]
Type: filesandordirs; Name: "{app}\steamcmd"
[Code]
{ ----------------------------------------------------------------------------
  Launch gating (fix: "Internal error: CallSpawnServer: Unexpected
  response: $0").

  When the user chooses the "all users" install mode, Setup respawns
  itself elevated and hosts an internal spawn server inside the original
  non-elevated process. The post-install "launch now" entry runs through
  that channel (postinstall implies runasoriginaluser); if the original
  process is gone, the call fails and aborts the finished installation.

  The spawn server only ever exists in an elevated (respawned) Setup, so
  "running with admin rights" implies "the spawn channel would be used".
  Note the /SPAWNWND= parameter itself is unusable as a test: it is
  filtered out before the command line is exposed to [Code]. Shortcuts
  are created in every mode, so skipping the entry there loses nothing.
---------------------------------------------------------------------------- }
function CanLaunchAfterInstall(): Boolean;
begin
  Result := not IsAdminLoggedOn;
end;

{ ----------------------------------------------------------------------------
  Previous-version cleanup.

  Older releases (e.g. 2.0.4) used a different AppId, so Setup would
  otherwise install side by side and leave the old uninstall entry, files
  and shortcuts behind. Before copying any file we look for foreign
  "WorkshopDL*" uninstall entries in every registry view, run their
  uninstaller silently (falling back to direct removal), delete leftover
  known folders and old shortcuts, and preserve WorkshopDL.ini so the
  user's settings survive the upgrade.
---------------------------------------------------------------------------- }
const
  OwnAppId = '7B2F1F5B-3C1A-4E5A-9D4E-2C6A8B0F1D21';

var
  SavedOldIni: String;
  CleanupLeftovers: Boolean;

function IsOwnUninstallKey(const Subkey: String): Boolean;
begin
  Result := Pos(OwnAppId, UpperCase(Subkey)) > 0;
end;

{ Legacy leftovers from the 2.0.x Clickteam build. Removed on upgrade so the
  new single-app install stays clean. }
procedure RemoveOneLegacyFile(const Dir, Name: String);
var
  F: String;
begin
  if Dir = '' then Exit;
  F := AddBackslash(Dir) + Name;
  if FileExists(F) then
  begin
    if DeleteFile(F) then
      Log('Removed legacy file ' + F)
    else
      CleanupLeftovers := True;
  end;
end;

procedure RemoveOldAppFiles(const Dir: String);
begin
  RemoveOneLegacyFile(Dir, 'WorkshopDL.exe');
  RemoveOneLegacyFile(Dir, 'WorkshopDL.dat');
  RemoveOneLegacyFile(Dir, 'WorkshopDL.ini');
end;

function IsWorkshopDLName(const S: String): Boolean;
begin
  Result := (Length(S) >= 10) and (CompareText(Copy(S, 1, 10), 'WorkshopDL') = 0);
end;

function DirOfFile(const FilePath: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := Length(FilePath) downto 1 do
    if FilePath[I] = '\' then
    begin
      Result := Copy(FilePath, 1, I - 1);
      Exit;
    end;
end;

procedure SaveOldIni(const Dir: String);
var
  Src: String;
begin
  if (SavedOldIni = '') and (Dir <> '') then
  begin
    Src := AddBackslash(Dir) + 'WorkshopDL.ini';
    if FileExists(Src) then
    begin
      SavedOldIni := ExpandConstant('{tmp}') + '\WorkshopDL.prev.ini';
      CopyFile(Src, SavedOldIni, False);
      Log('Preserved settings from ' + Src);
    end;
  end;
end;

procedure ScanUninstallRoot(const Tag: String; Root: Cardinal; Entries: TStringList);
var
  Names: TArrayOfString;
  I: Integer;
  Base, DisplayName, UninstallString, InstallLocation: String;
begin
  Base := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\';
  if not RegGetSubkeyNames(Root, 'Software\Microsoft\Windows\CurrentVersion\Uninstall', Names) then
    Log('Scan of root ' + Tag + ' failed')
  else
  begin
    Log('Root ' + Tag + ': ' + IntToStr(Length(Names)) + ' subkeys');
    for I := 0 to Length(Names) - 1 do
    begin
      if IsOwnUninstallKey(Names[I]) then
        Continue;
      if RegQueryStringValue(Root, Base + Names[I], 'DisplayName', DisplayName) and
         IsWorkshopDLName(DisplayName) then
      begin
        UninstallString := '';
        InstallLocation := '';
        RegQueryStringValue(Root, Base + Names[I], 'UninstallString', UninstallString);
        RegQueryStringValue(Root, Base + Names[I], 'InstallLocation', InstallLocation);
        Entries.Add(Tag + ';' + Names[I] + ';' + UninstallString + ';' + InstallLocation);
        Log('Found previous version: ' + DisplayName + ' at ' + Tag + '\' + Names[I]);
      end;
    end;
  end;
end;

procedure RemoveDirIfLeftover(const Dir: String);
begin
  if (Dir <> '') and (CompareText(Dir, ExpandConstant('{app}')) <> 0) and DirExists(Dir) then
  begin
    SaveOldIni(Dir);
    Log('Removing leftover folder ' + Dir);
    if not DelTree(Dir, True, True, True) then
    begin
      Log('Could not remove ' + Dir);
      CleanupLeftovers := True;
    end;
  end;
end;

procedure RemoveOldShortcuts();
begin
  { shortcuts created by old all-users installs; ours are (re)created later }
  if DirExists(ExpandConstant('{commonprograms}\WorkshopDL')) then
    DelTree(ExpandConstant('{commonprograms}\WorkshopDL'), True, True, True);
  if FileExists(ExpandConstant('{commondesktop}\WorkshopDL.lnk')) then
    DeleteFile(ExpandConstant('{commondesktop}\WorkshopDL.lnk'));
  if FileExists(ExpandConstant('{commondesktop}\WorkshopDL Launcher.lnk')) then
    DeleteFile(ExpandConstant('{commondesktop}\WorkshopDL Launcher.lnk'));
end;

function RootFromTag(const Tag: String): Cardinal;
begin
  if Tag = 'CU32' then
    Result := HKCU32
  else if Tag = 'CU64' then
    Result := HKCU64
  else if Tag = 'LM32' then
    Result := HKLM32
  else
    Result := HKLM64;
end;

procedure RemoveOldVersion(Root: Cardinal; const Tag, Subkey, UninstallString, InstallLocation: String);
var
  KeyPath, UninstExe, Dir, Dummy: String;
  ResultCode: Integer;
begin
  KeyPath := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + Subkey;

  { Where did the old version live? }
  Dir := InstallLocation;
  UninstExe := RemoveQuotes(UninstallString);
  if UninstExe <> '' then
  begin
    if Dir = '' then
      Dir := DirOfFile(UninstExe);
    if (Length(Dir) >= 10) and (CompareText(Copy(Dir, Length(Dir) - 9, 10), '\uninstall') = 0) then
      SetLength(Dir, Length(Dir) - 10);
  end;

  SaveOldIni(Dir);

  { Run the old uninstaller: it knows all its own files, shortcuts and
    registry values. When the old version was installed for all users this
    triggers one UAC consent (or runs without prompt when we are elevated). }
  if (UninstExe <> '') and FileExists(UninstExe) then
  begin
    Log('Running previous uninstaller: ' + UninstExe);
    if Exec(UninstExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
            SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log('Previous uninstaller exit code: ' + IntToStr(ResultCode))
    else
      Log('Could not start the previous uninstaller');
  end;

  { Fallback: remove whatever is left of the old version. }
  Dummy := '';
  if RegQueryStringValue(Root, KeyPath, 'UninstallString', Dummy) then
  begin
    Log('Removing leftover uninstall key ' + Tag + '\' + Subkey);
    RegDeleteKeyIncludingSubkeys(Root, KeyPath);
    Dummy := '';
    if RegQueryStringValue(Root, KeyPath, 'UninstallString', Dummy) then
    begin
      Log('Uninstall key still present after removal (insufficient rights?)');
      CleanupLeftovers := True;
    end;
  end;
  RemoveDirIfLeftover(Dir);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Entries: TStringList;
  I, P: Integer;
  Encoded, Tag, Subkey, UninstallString, InstallLocation: String;
  Root: Cardinal;
begin
  Result := '';
  SavedOldIni := '';
  CleanupLeftovers := False;
  Entries := TStringList.Create;
  try
    Log('PrepareToInstall: scanning for previous WorkshopDL versions');
    ScanUninstallRoot('CU32', HKCU32, Entries);
    ScanUninstallRoot('CU64', HKCU64, Entries);
    ScanUninstallRoot('LM32', HKLM32, Entries);
    ScanUninstallRoot('LM64', HKLM64, Entries);
    Log(IntToStr(Entries.Count) + ' previous WorkshopDL installation(s) found');
    for I := 0 to Entries.Count - 1 do
    begin
      Encoded := Entries[I];
      Tag := '';
      Subkey := '';
      UninstallString := '';
      InstallLocation := '';
      P := Pos(';', Encoded);
      if P > 0 then
      begin
        Tag := Copy(Encoded, 1, P - 1);
        Delete(Encoded, 1, P);
      end;
      P := Pos(';', Encoded);
      if P > 0 then
      begin
        Subkey := Copy(Encoded, 1, P - 1);
        Delete(Encoded, 1, P);
      end;
      P := Pos(';', Encoded);
      if P > 0 then
      begin
        UninstallString := Copy(Encoded, 1, P - 1);
        Delete(Encoded, 1, P);
      end;
      InstallLocation := Encoded;
      Root := RootFromTag(Tag);
      RemoveOldVersion(Root, Tag, Subkey, UninstallString, InstallLocation);
    end;
    { Known folders that may remain without a registry entry }
    RemoveDirIfLeftover(ExpandConstant('{commonpf32}\WorkshopDL'));
    RemoveDirIfLeftover(ExpandConstant('{commonpf64}\WorkshopDL'));
    { Also strip the legacy single-app files from the target dir itself. }
    RemoveOldAppFiles(ExpandConstant('{app}'));
    RemoveOldShortcuts();
    if CleanupLeftovers then
      MsgBox(ExpandConstant('{cm:CleanupFailed}'), mbInformation, MB_OK);
  finally
    Entries.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { Put the preserved settings back after the files have been copied
    (the shipped WorkshopDL.ini is only copied when none exists). }
  if (CurStep = ssPostInstall) and (SavedOldIni <> '') and FileExists(SavedOldIni) then
  begin
    if CopyFile(SavedOldIni, ExpandConstant('{app}\WorkshopDL.ini'), False) then
      Log('Restored previous WorkshopDL.ini')
    else
      Log('Could not restore previous WorkshopDL.ini');
  end;
end;
