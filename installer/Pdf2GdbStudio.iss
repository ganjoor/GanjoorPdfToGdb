; Inno Setup 6 script (https://jrsoftware.org/isinfo.php). Built by build-installer.ps1, which passes /DAppVersion and /DSourceDir.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#define AppName "Pdf2Gdb Studio"
#define AppExe "Pdf2GdbStudio.exe"

[Setup]
; keep this GUID forever: it lets a new version upgrade the old one in place
AppId={{6C1B7E52-3F0A-4B7D-9A55-2D8E4F1C7A90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Ganjoor
AppPublisherURL=https://ganjoor.net
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\GanjoorPdfToGdb.WinForms\app.ico
OutputDir=output
OutputBaseFilename=Pdf2GdbStudio-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; installs for the current user without an administrator prompt; the user can choose "for all users" in the dialog
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; both shortcuts are on by default
Name: "desktopicon"; Description: "ساخت میانبر روی دسکتاپ (Create a desktop shortcut)"; GroupDescription: "Shortcuts:"
Name: "startmenuicon"; Description: "ساخت میانبر در منوی Start (Create a Start menu shortcut)"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autodesktop}\تبدیل کتاب به گنجور"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startmenuicon
Name: "{autoprograms}\حذف {#AppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "اجرای {#AppName} (Launch {#AppName})"; Flags: nowait postinstall skipifsilent
