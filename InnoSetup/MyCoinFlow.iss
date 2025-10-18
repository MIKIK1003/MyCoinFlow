; ================== MyCoinFlow Setup (Inno Setup 6) ==================
; Hersteller: Brugilim | Produkt: MyCoinFlow | Version: 1.2.1 (FIX)
; Installiert: App (self-contained publish), Templates, Prereqs (LocalDB 2022, Tesseract)

#define AppName        "MyCoinFlow"
#define AppVersion     "1.2.18"
#define Manufacturer   "brugilimSoft"

; ====== HARDE Pfade (bitte prüfen/anpassen, falls nötig) ======
#define SrcPublish     "C:\Dev\MyCoinFlow\installer\publish\win-x64"
#define SrcTemplates   "C:\Dev\MyCoinFlow\Installer\MyCoinFlow.Msi\Templates"
#define SrcLocalDB     "C:\Dev\MyCoinFlow\Installer\MyCoinFlow.Bundle\Packages\SqlLocalDB2022-x64.exe"
#define SrcTesseract   "C:\Dev\MyCoinFlow\Installer\MyCoinFlow.Bundle\Packages\tesseract-ocr-w64-setup-5.5.0.20241111.exe"

[Setup]
AppId="5F8C6A1F-2A6E-4C0D-9E9E-4A9E6D8F4B7C"
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Manufacturer}
DefaultDirName={pf64}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=MyCoinFlow-Setup
OutputDir="C:\Users\miche\OneDrive\Dokumente\MyCoinFlowUpdate"
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
CloseApplications=force
SetupLogging=yes
UninstallDisplayIcon={app}\MyCoinFlow.exe
WizardStyle=modern

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; ---- App Publish (alles aus dem publish-Ordner) ----
Source: "{#SrcPublish}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; ---- Templates (unter {app}\Templates) ----
Source: "{#SrcTemplates}\MyCoinFlowDB.mdf"; DestDir: "{app}\Templates"; Flags: ignoreversion
Source: "{#SrcTemplates}\MyCoinFlowDB_log.ldf"; DestDir: "{app}\Templates"; Flags: ignoreversion

; ---- Prereqs: werden im [Run]-Abschnitt bedingt ausgeführt | 'external' = nicht einbetten, 'dontcopy' = direkt von Quelle starten
Source: "{#SrcLocalDB}"; DestDir: "{tmp}"; Flags: external dontcopy
Source: "{#SrcTesseract}"; DestDir: "{tmp}"; Flags: external dontcopy

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\MyCoinFlow.exe"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\MyCoinFlow.exe"; Tasks: desktopicon

[Run]
[Run]
Filename: "{#SrcLocalDB}"; Parameters: "/quiet /norestart /IACCEPTSQLLOCALDBLICENSETERMS=YES"; StatusMsg: "Installiere SQL Server Express LocalDB 2022..."; Check: not IsLocalDBInstalled; Flags: waituntilterminated
Filename: "{#SrcTesseract}"; Parameters: "/VERYSILENT /NORESTART"; StatusMsg: "Installiere Tesseract OCR..."; Check: not IsTesseractPresent; Flags: waituntilterminated


[UninstallRun]
; (optional) Tesseract nicht deinstallieren – Nutzer könnte es noch brauchen.
; (nichts tun)

[Registry]
; (optional) hier könntest du Produktinfos ablegen – nicht nötig.

; ======== Post-Compile: version.json im OneDrive-Ordner schreiben ========
; Lokaler Ordner, in dem Setup.exe und version.json nebeneinander liegen sollen
#define OneDriveUpdateDir "C:\Users\miche\OneDrive\Dokumente\MyCoinFlowUpdate"

; Direkter Online-Downloadlink zur EXE (mscontent-Link aus Chrome „Downloadlink kopieren“)
#define SetupDownloadUrl  "https://onedrive.live.com/personal/74e7b5071216d03a/_layouts/15/download.aspx?SourceUrl=%2Fpersonal%2F74e7b5071216d03a%2FDocuments%2FDokumente%2FMyCoinFlowUpdate%2FMyCoinFlow%2DSetup%2Eexe"

; Release Notes optional (einzeilig, minifiziert)
#define ReleaseNotes      " "

#define JsonPath AddBackslash(OneDriveUpdateDir) + "version.json"
#define JsonText  "{" + """version"": """ + AppVersion + """, " + \
                 """notes"": """ + ReleaseNotes + """, " + \
                 """fileUrl"": """ + SetupDownloadUrl + """" + "}"

#expr SaveStringToFile(JsonPath, JsonText, False)
; ========================================================================



[Code]
function IsLocalDBInstalled: Boolean;
var
  Versions: TArrayOfString;
begin
  { Prüfe Registry-Pfade für LocalDB (64-bit Ansicht) }
  Result := RegGetSubkeyNames(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions', Versions);
end;

function IsTesseractPresent: Boolean;
begin
  { Standardpfad: C:\Program Files\Tesseract-OCR\tesseract.exe }
  Result := FileExists(ExpandConstant('{pf64}\Tesseract-OCR\tesseract.exe'));
end;

procedure InitializeWizard;
begin
  { harte Checks: Quelle muss existieren, sonst abbrechen mit klarer Meldung }
  if not DirExists('{#SrcPublish}') then
    MsgBox('Publish-Ordner fehlt: ' + '{#SrcPublish}' + #13#10 +
           'Bitte zuerst bauen: dotnet publish -c Release -r win-x64 --self-contained true -o ' + '{#SrcPublish}', mbCriticalError, MB_OK);
end;



