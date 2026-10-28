[Setup]
AppName=Snake3D
AppVersion=1.0
AppPublisher=Snake3D
DefaultDirName={autopf}\Snake3D
DefaultGroupName=Snake3D
OutputDir=C:\Users\Admin\Desktop\installer
OutputBaseFilename=Snake3DSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=image\Crystal_Hunter.ico
UninstallDisplayIcon={app}\Crystal_Hunter.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "C:\Users\Admin\Desktop\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "image\Crystal_Hunter.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Snake3D"; Filename: "{app}\Snake3D.exe"; IconFilename: "{app}\Crystal_Hunter.ico"
Name: "{autodesktop}\Snake3D"; Filename: "{app}\Snake3D.exe"; IconFilename: "{app}\Crystal_Hunter.ico"

[Run]
Filename: "{app}\Snake3D.exe"; Description: "Запустить Snake3D"; Flags: nowait postinstall skipifsilent