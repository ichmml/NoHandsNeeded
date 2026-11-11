@echo off
rem Baut NoHandsNeeded.exe mit dem C#-Compiler, der in Windows schon enthalten ist.
rem Keine Installation noetig.
rem Zusaetzlich entsteht installer\NoHandsNeededAT.exe (mit uiAccess) fuer den Installer und den Sperrbildschirm.
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C#-Compiler nicht gefunden. Ist .NET Framework 4.8 installiert?
  pause
  exit /b 1
)

set "RES=/resource:Resources\quadstick_beispiel.png,quadstick_beispiel.png"
for %%f in (Resources\wortliste_*.txt.gz) do set "RES=!RES! /resource:%%f,%%~nxf"
for %%f in (Resources\Texte\*.txt) do set "RES=!RES! /resource:%%f,texte_%%~nxf"
for %%f in (Anleitung\Anleitung-*.txt) do set "RES=!RES! /resource:%%f,%%~nxf"
set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Accessibility.dll /r:System.Security.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll"

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:NoHandsNeeded.exe /win32icon:Resources\tastatur.ico /win32manifest:Resources\app.manifest !RES! %REFS% src\*.cs
if errorlevel 1 goto fehler

rem Variante fuer den Installer: darf auf den Sperrbildschirm und in Programme mit Adminrechten tippen.
rem Funktioniert nur signiert und unter "C:\Program Files" installiert.
if not exist installer mkdir installer
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:installer\NoHandsNeededAT.exe /win32icon:Resources\tastatur.ico /win32manifest:Resources\app-uiaccess.manifest !RES! %REFS% src\*.cs
if errorlevel 1 goto fehler

rem Signieren (spaeter, mit eigenem Zertifikat), z. B.:
rem signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n "MDM3D" NoHandsNeeded.exe installer\NoHandsNeededAT.exe

echo.
echo Fertig: %CD%\NoHandsNeeded.exe
echo         %CD%\installer\NoHandsNeededAT.exe (fuer den Installer)
if "%CI%"=="" pause
exit /b 0

:fehler
echo.
echo Bauen fehlgeschlagen.
if "%CI%"=="" pause
exit /b 1
