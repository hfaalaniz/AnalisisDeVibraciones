; Instalador de «Análisis de Vibraciones y Balanceo» (Inno Setup 6)
; Autor: Fabian Alaniz – ElectroNet · www.developers-soft.com
; Compilar (desde la raíz del repo, después de «dotnet publish … -o publish\AnalisisVibraciones»):
;   iscc /DAppVersion=3.0.0 installer\AnalisisVibraciones.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Análisis de Vibraciones y Balanceo"
#define AppShort "Análisis de Vibraciones"
#define AppExe "AnalisisVibraciones.exe"

[Setup]
AppId={{8F3C2A51-6D4B-4E8A-9C1F-2B7D5E0A4C93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Fabian Alaniz – ElectroNet
AppPublisherURL=https://www.developers-soft.com/
AppSupportURL=https://www.developers-soft.com/
AppUpdatesURL=https://github.com/hfaalaniz/AnalisisDeVibraciones/releases
AppCopyright=© 2025-2026 Fabian Alaniz · ElectroNet
DefaultDirName={autopf}\Analisis de Vibraciones
DefaultGroupName={#AppShort}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=AnalisisVibraciones-{#AppVersion}-instalador
SetupIconFile=..\src\VibrationAnalysis.UI\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Por defecto para todos los usuarios; el instalador ofrece instalar solo para el usuario actual (sin permisos de administrador)
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=ElectroNet
VersionInfoDescription=Instalador de {#AppName}
VersionInfoProductName={#AppName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assocvaproj"; Description: "Abrir los proyectos de balanceo (.vaproj) con esta aplicación"; GroupDescription: "Asociaciones:"
Name: "firewall"; Description: "Permitir conexiones entrantes en el firewall (servidor Modbus TCP para PLC)"; GroupDescription: "Red:"; Flags: unchecked; Check: IsAdminInstallMode

[Files]
Source: "..\publish\AnalisisVibraciones\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENCIA.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppShort}"; Filename: "{app}\{#AppExe}"; Comment: "Análisis de vibraciones, diagnóstico y balanceo de campo"
Name: "{autodesktop}\{#AppShort}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.vaproj"; ValueType: string; ValueName: ""; ValueData: "AnalisisVibraciones.Proyecto"; Flags: uninsdeletevalue; Tasks: assocvaproj
Root: HKA; Subkey: "Software\Classes\AnalisisVibraciones.Proyecto"; ValueType: string; ValueName: ""; ValueData: "Proyecto de balanceo"; Flags: uninsdeletekey; Tasks: assocvaproj
Root: HKA; Subkey: "Software\Classes\AnalisisVibraciones.Proyecto\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assocvaproj
Root: HKA; Subkey: "Software\Classes\AnalisisVibraciones.Proyecto\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocvaproj

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Analisis de Vibraciones"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppShort}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Analisis de Vibraciones"""; Flags: runhidden; RunOnceId: "QuitarFirewall"; Check: IsAdminInstallMode

; Los datos del usuario (%LOCALAPPDATA%\AnalisisVibraciones: base de tendencias, configuraciones, temas) se conservan al desinstalar.
