; Inno Setup script for the LumosCast installer.
;
; Build the payload first, then compile this on Windows (Inno Setup 6):
;   src/LumosPresenter.Launcher/package-windows.sh win-x64
;   iscc src\LumosPresenter.Launcher\package-windows.iss
; Produces artifacts/LumosCast-1.0.0-setup.exe.
;
; Installs PER USER, into %LOCALAPPDATA%, deliberately. The WebHost resolves
; Data:DatabasePath ("data/lumos.db") relative to its content root — which is the install
; directory — and writes to it at runtime. Under %ProgramFiles% that file is read-only for
; a standard user, so the app would install cleanly and then fail to save. Per-user also
; means no UAC prompt on install.

#define AppName "LumosCast"
#define AppVersion "1.0.0"
#define AppExe "LumosPresenter.Launcher.exe"
#define Payload "..\..\artifacts\LumosCast-win-x64"

[Setup]
AppId={{8F3C1E7A-4B2D-4E6A-9C15-2A7D5E9B1C04}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\..\artifacts
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=Assets\app.ico
; Per-user install: no admin rights, and the install dir stays writable so the
; verse database and imported songs can be saved.
PrivilegesRequired=lowest
; Plain "x64" rather than "x64compatible": the latter needs Inno Setup 6.3+ and
; fails to compile on 6.0-6.2, which is a confusing error to hit on the build box.
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; The payload is ~600MB, mostly the speech model, which compresses poorly and slowly.
Compression=lzma2/fast
SolidCompression=yes
DisableProgramGroupPage=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything package-windows.sh produced: both executables, the .NET runtime, the
; speech model, and the verse database.
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Logs only. data/ is deliberately NOT removed: lumos.db holds the operator's imported
; songs and their cached api.bible translations, so wiping it on uninstall would destroy
; the library of anyone who uninstalls to reinstall. Inno already removes the lumos.db it
; installed (it is in [Files]); what survives is what the operator added, which is theirs.
; The leftover data/ folder is the intended cost of not deleting a song library.
Type: filesandordirs; Name: "{app}\logs"
