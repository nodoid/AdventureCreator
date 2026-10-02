; Inno Setup script for the Windows installers (x64 and ARM64) of Adventure System Studio, for distribution outside
; the Microsoft Store. Build the self-contained Studio first, then compile this with ISCC:
;
;   ISCC.exe /DSourceDir=<publish folder> /DAppVersion=1.0 /DOutputDir=<folder> [/DArch=arm64] build\studio-installer.iss
;
; SourceDir is a self-contained, unpackaged publish for the same architecture (-p:StudioStandalone=true, plus
; -p:StudioArch=arm64 for ARM), so the installed app needs neither .NET nor the Windows App SDK on the PC.
; The x64 installer also runs on ARM PCs (under emulation); the ARM64 one installs only on ARM PCs.

#ifndef SourceDir
  #error Pass /DSourceDir=<self-contained publish folder for this architecture>
#endif
#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#if Arch == "arm64"
  #define ArchAllowed "arm64"
#elif Arch == "x64"
  #define ArchAllowed "x64compatible"
#else
  #error Arch must be x64 or arm64
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
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchAllowed}
MinVersion=10.0.17763
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=AdventureSystemStudio-{#AppVersion}-{#Arch}-Setup
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
