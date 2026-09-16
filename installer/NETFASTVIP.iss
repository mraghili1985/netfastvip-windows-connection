#define MyAppName "NETFASTVIP"
#define MyAppVersion "1.0.4"
#define MyAppExeName "NETFASTVIP.exe"
#define MyAppUrl "https://t.me/netfastvip"
#define PublishDir "..\bin\Release\net10.0-windows\win-x64\publish"
#define OpenVpnMsi "OpenVPN-2.7.6-amd64.msi"

[Setup]
AppId={{B7F3A2C4-9D41-4E8A-A1F6-3C2E7D5B8901}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=NETFASTVIP
AppPublisherURL={#MyAppUrl}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=NETFASTVIP
VersionInfoDescription=NETFASTVIP VPN Client Setup
VersionInfoProductName=NETFASTVIP VPN Client
DefaultDirName={autopf}\NETFASTVIP
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
OutputDir=output
OutputBaseFilename=NETFASTVIP-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "config.json,auth.txt,mgmt-pass.txt,gen-*.ovpn,inline-*.ovpn,netfastvip-package.json,*.log"
Source: "config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "netfastvip-package.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "Fonts\*"; DestDir: "{app}\Fonts"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#OpenVpnMsi}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not OpenVpnInstalled

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\PolicyAgent"; ValueType: dword; ValueName: "AssumeUDPEncapsulationContextOnSendRule"; ValueData: 2

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#OpenVpnMsi}"" ADDLOCAL=OpenVPN,Drivers,Drivers.OvpnDco /qn /norestart"; StatusMsg: "Installing OpenVPN core..."; Check: not OpenVpnInstalled
Filename: "cmd.exe"; Parameters: "/c del ""{commondesktop}\OpenVPN GUI.lnk"""; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function OpenVpnInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\OpenVPN\bin\openvpn.exe'));
end;
