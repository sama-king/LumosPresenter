; Inno Setup script for the LumosCast installer.
;
; package.sh compiles this itself when Inno Setup 6's iscc is on the machine (Windows only):
;   src/LumosPresenter.Launcher/package.sh win-x64
; or build the payload there and compile it by hand:
;   iscc /DAppVersion=1.0.0 /DRid=win-x64 src\LumosPresenter.Launcher\package-windows.iss
; Produces artifacts/LumosCast-<version>-<rid>-setup.exe.
;
; Installs PER USER, into %LOCALAPPDATA%, deliberately. The WebHost resolves
; Data:DatabasePath ("data/lumos.db") relative to its content root — which is the install
; directory — and writes to it at runtime. Under %ProgramFiles% that file is read-only for
; a standard user, so the app would install cleanly and then fail to save. Per-user also
; means no UAC prompt on install.

#define AppName "LumosCast"
; package.sh passes the version from the Launcher csproj; these defaults are for a by-hand run.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Rid
  #define Rid "win-x64"
#endif
#define AppExe "LumosPresenter.Launcher.exe"
#define Payload "..\..\artifacts\LumosCast-" + Rid
#if Rid == "win-arm64"
  #define Arch "arm64"
#else
  #define Arch "x64"
#endif

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
OutputBaseFilename={#AppName}-{#AppVersion}-{#Rid}-setup
SetupIconFile=Assets\app.ico
; Per-user install: no admin rights, and the install dir stays writable so the
; verse database and imported songs can be saved.
PrivilegesRequired=lowest
; Plain "x64" rather than "x64compatible": the latter needs Inno Setup 6.3+ and
; fails to compile on 6.0-6.2, which is a confusing error to hit on the build box.
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
; The payload is mostly the speech models, which compress poorly and slowly.
Compression=lzma2/fast
SolidCompression=yes
DisableProgramGroupPage=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything package.sh produced: both executables, the .NET runtime, the speech
; models, the default backgrounds and the translation seeds.
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
; the library of anyone who uninstalls to reinstall. The app creates lumos.db on first
; start, so Inno never installed it and never removes it; only the seed files it did
; install go. The leftover data/ folder is the intended cost of not deleting a song library.
; media/ stays for the same reason: it holds the operator's uploaded backgrounds.
Type: filesandordirs; Name: "{app}\logs"
