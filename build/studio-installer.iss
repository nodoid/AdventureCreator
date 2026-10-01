; Inno Setup script for the Windows (x64) installer of Adventure System Studio, for distribution outside the
; Microsoft Store. Build the self-contained Studio first, then compile this with ISCC:
;
;   ISCC.exe /DSourceDir=<publish folder> /DAppVersion=1.0 /DOutputDir=<folder> build\studio-installer.iss
;
; SourceDir is a self-contained, unpackaged win-x64 publish (WindowsPackageType=None, SelfContained and
; WindowsAppSDKSelfContained), so the installed app needs neither .NET nor the Windows App SDK on the PC.

#ifndef SourceDir
  #error Pass /DSourceDir=<self-contained win-x64 publish folder>
#endif
#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "Adventure System Studio"
#define AppExe "AdventureSystem.Studio.exe"

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{9DD83CF1-730B-49E0-A554-4AF46151A7E8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Paul F. Johnson
AppCopyright=Copyright © 2026 Paul F. Johnson
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=AdventureSystemStudio-{#AppVersion}-x64-Setup
#ifdef SetupIcon
SetupIconFile={#SetupIcon}
#endif
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
