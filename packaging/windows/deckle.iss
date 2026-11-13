; Windows installer (Inno Setup 6). Built by the release workflow from the win-x64 publish folder:
;   ISCC /DAppVersion=0.2.0 /DSourceDir=C:\path\to\win-x64 /Oout packaging\windows\deckle.iss
; Installs for the current user without an administrator prompt; "Install for all users" is offered in the dialog.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\win-x64"
#endif

[Setup]
; Never change AppId: it is how an update finds the installed copy.
AppId={{6F0B3C52-7B1D-4E0A-9C36-2A8D5E41D7B9}
AppName=Deckle
AppVersion={#AppVersion}
AppVerName=Deckle {#AppVersion}
AppPublisher=Chris Servin
AppPublisherURL=https://deckle.cjservin.com
AppSupportURL=https://github.com/noddle-Cake/deckle/issues
AppUpdatesURL=https://github.com/noddle-Cake/deckle/releases
DefaultDirName={autopf}\Deckle
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=deckle-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\Deckle.App\Assets\deckle.ico
UninstallDisplayIcon={app}\Deckle.exe
UninstallDisplayName=Deckle
VersionInfoVersion={#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; An update closes a running Deckle first and starts it again afterwards.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole self-contained build, licenses folder included (it has to ship with every copy).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Deckle"; Filename: "{app}\Deckle.exe"
Name: "{autodesktop}\Deckle"; Filename: "{app}\Deckle.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Deckle.exe"; Description: "{cm:LaunchProgram,Deckle}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The shortcut Deckle creates itself for the scan hotkey (Settings). Scans and settings are left alone.
Type: files; Name: "{userprograms}\Deckle Scan.lnk"
