; Inno Setup 6 - installation par utilisateur, sans droits administrateur.
#define AppName "StackR"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

[Setup]
AppId={{B7C5E0A4-3F6D-4C1E-9A52-5C7D1E8F2A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Ga3TH
AppPublisherURL=https://github.com/Ga3TH/StackR
DefaultDirName={localappdata}\Programs\StackR
DefaultGroupName=StackR
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename=StackR-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\StackR.App.exe
DisableProgramGroupPage=yes
; L'application crée le mutex « Local\StackR.App » (SingleInstanceGuard.MutexName). Setup et Uninstall
; cherchent le nom tel quel (sensible à la casse) ; un nom sans préfixe est résolu dans l'espace de noms
; de la session courante, c'est-à-dire « Local\ » : « StackR.App » désigne donc exactement ce mutex.
; Le préfixe n'est pas écrit : dans cette directive, la barre oblique inverse sert d'échappement (virgule).
; Installation par utilisateur (PrivilegesRequired=lowest) : Setup tourne dans la même session que l'app.
AppMutex=StackR.App
; Mises à jour : Setup propose de fermer StackR (verrous sur les fichiers) puis ne le relance pas de force.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\StackR\*"; DestDir: "{app}"; Excludes: "packages\*"; Flags: ignoreversion recursesubdirs createallsubdirs
; Paquets des composants par défaut (installation hors ligne) : déjà compressés (zip), donc stockés tels quels.
Source: "..\publish\StackR\packages\*"; DestDir: "{app}\packages"; Flags: ignoreversion nocompression skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\StackR"; Filename: "{app}\StackR.App.exe"
Name: "{autodesktop}\StackR"; Filename: "{app}\StackR.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StackR.App.exe"; Description: "{cm:LaunchProgram,StackR}"; Flags: nowait postinstall skipifsilent
; Mise à jour depuis l'application (installation silencieuse /SILENT) : la case « Lancer » n'existe pas
; (postinstall + skipifsilent), donc on relance StackR nous-mêmes (skipifnotsilent) avec --restarted
; (la nouvelle instance attend la sortie de l'ancienne, qui tenait le mutex).
Filename: "{app}\StackR.App.exe"; Parameters: "--restarted"; Flags: nowait skipifnotsilent
