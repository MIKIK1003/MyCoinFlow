; ================== MyCoinFlow Setup (Inno Setup 6) ==================
; Hersteller: Brugilim | Produkt: MyCoinFlow | Version: 1.2.1 (FIX)
; Installiert: App (self-contained publish), Templates, Prereqs (LocalDB 2022, Tesseract)

#define AppName        "MyCoinFlow"
#define AppVersion     "1.2.12"
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
#define OneDriveUpdateDir "C:\Users\miche\OneDrive\Dokumente\MyCoinFlowUpdate"
#define SetupDownloadUrl  "https://my.microsoftpersonalcontent.com/personal/74e7b5071216d03a/_layouts/15/download.aspx?UniqueId=17186074-cf0e-4759-8998-f2412b40d307&Translate=false&tempauth=v1e.eyJzaXRlaWQiOiIzOGJkN2Q4OS04YzAzLTRjZGEtOWIwZi00ZjRlNzZlODdlMmUiLCJhcHBpZCI6IjAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLTAwMDA0ODE3MTBhNCIsImF1ZCI6IjAwMDAwMDAzLTAwMDAtMGZmMS1jZTAwLTAwMDAwMDAwMDAwMC9teS5taWNyb3NvZnRwZXJzb25hbGNvbnRlbnQuY29tQDkxODgwNDBkLTZjNjctNGM1Yi1iMTEyLTM2YTMwNGI2NmRhZCIsImV4cCI6IjE3NjA2NTAyNzYifQ.NYG2TmcQYSToNm0MT5eLCEsNdJc_jtrBQzXcpdDkf08wUlWaMCZ5Wi_QORoSGFmcLlyvt6cbJbKCicmoJ8NzEDfJrXZENSRs9Jwl-1yorzqEKs9WRDnFKPwdC4eYnZg0XzM-KCA6-fnWk-u72NUdj8BJdXxnNTpgnCWXnSYcwnbfnVJdd0zwlnAQPvKO_g8v2_H1rEjoGLMVWzyp4hmzF9cmoUwXUSdkxP84jk5w_9-c6NVKllZZQPu89tHz-JJr3G445FGqWcRUvbZHZpoaENPGVWiXLf206EPEdZ0L0seKh409MauVAJ848Nv2glX-N1Mq2yXeJaIyzQrsFtB802Y7dFctDZ0tde5v__n-IqTGR4nh2yRu73FVcg-m8E4qUrABJJ7_wGje3cZt7zgnUebTfizQX90iu4ANHJ--pEc.W8iXK-sGKOrCVhC4SoMxxJ_AAspx_bUF00m0ZvCrRUo&ApiVersion=2.0&AVOverride=1"
#define ReleaseNotes      " "  ; optional

#define JsonPath AddBackslash(OneDriveUpdateDir) + "version.json"
#define JsonText "{" + """version"": """ + AppVersion + """, " + """notes"": """ + ReleaseNotes + """, " + """fileUrl"": """ + SetupDownloadUrl + """" + "}"

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



