; Script de Inno Setup para Simutronik (Windows 64 bits)

[Setup]
AppId={{8D6B4A2E-1C5B-4B9E-9A2F-4E7C1F8B9011}
AppName=Simutronik
AppVersion=1.1.0
AppVerName=Simutronik 1.1
AppPublisher=Andrés Adolfo Moresco Skupieñ
DefaultDirName={autopf}\Simutronik
DefaultGroupName=Simutronik
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=c:\Dev\VersionParaAntiGravity12-11\simutronik\assets\simutronik.ico
LicenseFile=c:\Dev\VersionParaAntiGravity12-11\simutronik\installer\LICENCIA_USO.txt
UninstallDisplayIcon={app}\Simutronik.exe
OutputDir=c:\Dev\Instalador_Simutronik
OutputBaseFilename=Instalar_Simutronik
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "projectsdesktop"; Description: "Crear acceso directo a los Proyectos de ejemplo en el Escritorio"; GroupDescription: "Contenido incluido:"; Flags: unchecked
Name: "demovideos"; Description: "Incluir videos de demostración en la pantalla de inicio"; GroupDescription: "Contenido incluido:"

[Files]
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\Simutronik.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\LEEME.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\VersionParaAntiGravity12-11\simutronik\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\VersionParaAntiGravity12-11\simutronik\installer\LICENCIA_USO.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\VersionParaAntiGravity12-11\simutronik\docs\Terminos_y_Licencia_de_Uso_de_Simutronik.pdf"; DestDir: "{app}"; Flags: ignoreversion
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\data_ElectricSim_windows_x86_64\*"; DestDir: "{app}\data_ElectricSim_windows_x86_64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\Proyectos\*"; DestDir: "{app}\Proyectos"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "c:\Dev\Instalador_Simutronik\Simutronik_Portable\Sketches\*"; DestDir: "{app}\Sketches"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "C:\Users\andre\AppData\Roaming\Godot\app_userdata\ElectricSim\inicio_videos\_convertidos\*_v2.ogv"; DestDir: "{userappdata}\Godot\app_userdata\ElectricSim\inicio_videos"; Flags: onlyifdoesntexist; Tasks: demovideos

[Icons]
Name: "{group}\Simutronik"; Filename: "{app}\Simutronik.exe"
Name: "{group}\Proyectos de ejemplo"; Filename: "{app}\Proyectos"
Name: "{group}\Sketches Arduino (.hex)"; Filename: "{app}\Sketches"
Name: "{group}\Términos y Licencia de Uso"; Filename: "{app}\LICENCIA_USO.txt"
Name: "{group}\{cm:UninstallProgram,Simutronik}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Simutronik"; Filename: "{app}\Simutronik.exe"; Tasks: desktopicon
Name: "{autodesktop}\Proyectos Simutronik"; Filename: "{app}\Proyectos"; Tasks: projectsdesktop

[Run]
Filename: "{app}\Simutronik.exe"; Description: "{cm:LaunchProgram,Simutronik}"; Flags: nowait postinstall skipifsilent
