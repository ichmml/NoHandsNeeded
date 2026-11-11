; Installer für NoHandsNeeded (Inno Setup 6, https://jrsoftware.org/isinfo.php)
; Wird nur gebraucht, damit die Tastatur auf dem Windows-Sperrbildschirm erscheinen kann.
; Voraussetzungen: build.cmd ausgeführt, installer\NoHandsNeededAT.exe signiert.
; Bauen: ISCC.exe installer\NoHandsNeeded.iss

#define AppName "NoHandsNeeded"
#define AppVersion "1.01"
#define AtKey "MDM3D_Bildschirmtastatur_v1"   ; interner Schlüssel, bleibt wegen Sperrbildschirm-Erkennung im Programm gleich

[Setup]
AppId={{6B0E4C1E-2F0B-4E7A-9C1D-8A7F3E5B2D10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=MDM3D
AppPublisherURL=https://github.com/ichmml/nohandsneeded
DefaultDirName={autopf}\NoHandsNeeded
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=.
OutputBaseFilename=NoHandsNeeded-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\NoHandsNeeded.exe
; SignTool=meinsigntool $f   (in Inno Setup unter Tools > Configure Sign Tools eintragen)

; Sprache des Installers folgt der Windows-Sprache, ohne Auswahlfenster
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "da"; MessagesFile: "compiler:Languages\Danish.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "cs"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.AtName=NoHandsNeeded (MDM3D)
en.AtDesc=On-screen keyboard for hands-free use, e.g. with the Quadstick.
en.StartKb=Start NoHandsNeeded
de.AtName=NoHandsNeeded (MDM3D)
de.AtDesc=Bildschirmtastatur für die Bedienung ohne Hände, z. B. mit dem Quadstick.
de.StartKb=NoHandsNeeded starten
nl.AtName=NoHandsNeeded (MDM3D)
nl.AtDesc=Schermtoetsenbord voor bediening zonder handen, bijv. met de Quadstick.
nl.StartKb=NoHandsNeeded starten
fr.AtName=NoHandsNeeded (MDM3D)
fr.AtDesc=Clavier virtuel pour une utilisation sans les mains, par ex. avec le Quadstick.
fr.StartKb=Lancer NoHandsNeeded
da.AtName=NoHandsNeeded (MDM3D)
da.AtDesc=Skærmtastatur til betjening uden hænder, f.eks. med Quadstick.
da.StartKb=Start NoHandsNeeded
pl.AtName=NoHandsNeeded (MDM3D)
pl.AtDesc=Klawiatura ekranowa do obsługi bez rąk, np. z Quadstickiem.
pl.StartKb=Uruchom NoHandsNeeded
cs.AtName=NoHandsNeeded (MDM3D)
cs.AtDesc=Klávesnice na obrazovce pro ovládání bez rukou, např. s Quadstickem.
cs.StartKb=Spustit NoHandsNeeded
it.AtName=NoHandsNeeded (MDM3D)
it.AtDesc=Tastiera su schermo per l'uso senza mani, ad es. con il Quadstick.
it.StartKb=Avvia NoHandsNeeded
es.AtName=NoHandsNeeded (MDM3D)
es.AtDesc=Teclado en pantalla para usar sin manos, p. ej. con el Quadstick.
es.StartKb=Iniciar NoHandsNeeded
tr.AtName=NoHandsNeeded (MDM3D)
tr.AtDesc=Eller olmadan kullanım için ekran klavyesi, ör. Quadstick ile.
tr.StartKb=NoHandsNeeded'ı başlat
ru.AtName=NoHandsNeeded (MDM3D)
ru.AtDesc=Экранная клавиатура для управления без рук, например с Quadstick.
ru.StartKb=Запустить NoHandsNeeded

[Files]
Source: "NoHandsNeededAT.exe"; DestDir: "{app}"; DestName: "NoHandsNeeded.exe"; Flags: ignoreversion
Source: "..\Liesmich.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ReadMe.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Anleitung\*.txt"; DestDir: "{app}\Anleitung"; Flags: ignoreversion

[Dirs]
; Daten liegen bei installierter Version unter %APPDATA%\NoHandsNeeded (Programme ist schreibgeschützt)

[Icons]
Name: "{autoprograms}\NoHandsNeeded"; Filename: "{app}\NoHandsNeeded.exe"

[Registry]
; Anmeldung als Hilfsmittel (Ease of Access), damit Windows die Tastatur auf dem Sperrbildschirm starten darf
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{cm:AtName}"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "Description"; ValueData: "{cm:AtDesc}"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "ATExe"; ValueData: "NoHandsNeeded.exe"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "StartExe"; ValueData: "{app}\NoHandsNeeded.exe"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "StartParams"; ValueData: ""
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "Profile"; ValueData: "<HCIModel><Accommodation type=""severe dexterity"" /></HCIModel>"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "SimpleProfile"; ValueData: "osk"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: string; ValueName: "SecureDesktopAccommodation"; ValueData: "none"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: dword; ValueName: "TerminateOnDesktopSwitch"; ValueData: 1
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\{#AtKey}"; ValueType: dword; ValueName: "PassiveAutoStartBehavior"; ValueData: 1

[UninstallDelete]
; Eintrag "auf dem Sperrbildschirm anzeigen" entfernt die Tastatur beim Deinstallieren selbst nicht aus HKCU,
; das macht der Schalter in den Einstellungen. Windows ignoriert unbekannte Einträge.

[Run]
Filename: "{app}\NoHandsNeeded.exe"; Description: "{cm:StartKb}"; Flags: nowait postinstall skipifsilent
