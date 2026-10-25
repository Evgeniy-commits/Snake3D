[Setup]
AppName=Snake3D
AppVersion=1.0
AppPublisher=Snake3D
DefaultDirName={autopf}\Snake3D
DefaultGroupName=Snake3D
OutputDir=installer
OutputBaseFilename=Snake3DSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Snake3D.exe

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "bin\Release\Publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Snake3D"; Filename: "{app}\Snake3D.exe"
Name: "{autodesktop}\Snake3D"; Filename: "{app}\Snake3D.exe"

[Run]
Filename: "{app}\Snake3D.exe"; Description: "Запустить Snake3D"; Flags: nowait postinstall skipifsilent