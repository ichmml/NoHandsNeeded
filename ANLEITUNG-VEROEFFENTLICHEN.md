# Auf GitHub veröffentlichen

Alles geht im Browser, ohne Git-Programm.

## 1. Repository anlegen (einmalig)

1. https://github.com/new öffnen.
2. Name: **nohandsneeded**, Sichtbarkeit: **Public**.
3. Keinen Haken bei README, .gitignore oder Lizenz setzen, die sind schon im Ordner.
4. "Create repository" klicken.

Der Name sollte genau so lauten, weil README, Anleitungen und Installer auf
github.com/ichmml/nohandsneeded verweisen.

## 2. Dateien hochladen

1. Auf der leeren Repository-Seite den Link **"uploading an existing file"** anklicken.
2. Den **Inhalt** dieses Ordners (nicht den Ordner selbst) aus dem Explorer in das
   Browserfenster ziehen. Dazu gehört auch der Ordner `.github`. Ist er nicht zu sehen,
   im Explorer unter Anzeigen → Einblenden → "Ausgeblendete Elemente" aktivieren.
3. Unten "Commit changes" klicken.

GitHub nimmt im Browser höchstens 100 Dateien auf einmal. Der Ordner hat etwas mehr, also in
zwei Runden hochladen: zuerst die Ordner `src` und `tests`, dann "Add file" → "Upload files"
und den ganzen Rest.

## 3. Eine Version veröffentlichen (Release)

1. Im Repository rechts auf **"Releases"** → **"Create a new release"** klicken.
2. Bei "Choose a tag" eine neue Version eintippen, z. B. **v1.02**, und "Create new tag" wählen.
3. Titel, z. B. "Version 1.02", und darunter kurz die Neuerungen eintragen.
4. **"Publish release"** klicken.

GitHub baut die `NoHandsNeeded.exe` dann automatisch (unter "Actions" zu sehen, dauert etwa
1 bis 2 Minuten) und hängt sie an das Release. Wer die Tastatur haben will, lädt sie unter
Releases herunter.

Falls das automatische Bauen nicht klappt: Beim Release unter "Attach binaries" die
`NoHandsNeeded.exe` einfach von Hand hineinziehen.

## 4. Später etwas ändern

Datei im Repository anklicken → Stift-Symbol → ändern → "Commit changes".
Oder geänderte Dateien wie in Schritt 2 erneut hochladen, gleiche Namen werden ersetzt.
Danach eine neue Version wie in Schritt 3 anlegen, z. B. v1.02.
