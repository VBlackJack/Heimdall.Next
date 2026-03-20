; Heimdall.Next Inno Setup Script
; Produces a single .exe installer for end-user deployment.
; Supports Standard and SelfContained editions via preprocessor defines.

#ifndef Variant
  #define Variant "Standard"
#endif

#ifndef AppVersion
  #define AppVersion "2026.031812"
#endif

#ifndef SourceDir
  #define SourceDir "..\Dist\release\Heimdall.Next_build." + AppVersion + "_" + LowerCase(Variant)
  ; Maps: Standard -> _standard, SelfContained -> _selfcontained
#endif

[Setup]
AppId={{B7A4D3E1-8F2C-4A91-9D5E-6C3B8A1F0E72}
AppName=Heimdall.Next
AppVersion={#AppVersion}
AppVerName=Heimdall.Next v{#AppVersion}
AppPublisher=Julien Bombled
AppPublisherURL=https://github.com/VBlackJack/Heimdall.Next
DefaultDirName={autopf}\Heimdall.Next
DefaultGroupName=Heimdall.Next
AllowNoIcons=yes
OutputDir=..\Dist\installers
OutputBaseFilename=Heimdall.Next_{#AppVersion}_{#Variant}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Heimdall.Next.exe
SetupIconFile=..\src\Heimdall.App\app.ico
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Heimdall.Next"; Filename: "{app}\Heimdall.Next.exe"
Name: "{group}\{cm:UninstallProgram,Heimdall.Next}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Heimdall.Next"; Filename: "{app}\Heimdall.Next.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Heimdall.Next.exe"; Description: "{cm:LaunchProgram,Heimdall.Next}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  OldVersion: String;
begin
  // Check for existing installation and offer upgrade
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7A4D3E1-8F2C-4A91-9D5E-6C3B8A1F0E72}_is1', 'DisplayVersion', OldVersion) then
  begin
    if MsgBox('Heimdall.Next v' + OldVersion + ' is already installed.' + #13#10 + 'Do you want to upgrade to v{#AppVersion}?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;
