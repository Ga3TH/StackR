; Inno Setup 6 - installation par utilisateur, sans droits administrateur.
#define AppName "StackR"
#define AppVersion "0.1.0"

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
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\StackR.App.exe
DisableProgramGroupPage=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\StackR\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\StackR"; Filename: "{app}\StackR.App.exe"
Name: "{autodesktop}\StackR"; Filename: "{app}\StackR.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StackR.App.exe"; Description: "{cm:LaunchProgram,StackR}"; Flags: nowait postinstall skipifsilent
