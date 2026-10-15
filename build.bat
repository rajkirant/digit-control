@echo off
rem Builds DigitControl.exe with the C# compiler that ships with Windows
rem (.NET Framework 4), so nothing needs installing.

cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe. Turn on .NET Framework 4 in "Turn Windows features on or off".
    if not defined CI pause
    exit /b 1
)

rem Bluetooth and Wi-Fi use the Windows Runtime API, described by these files
set WINMD=%WINDIR%\System32\WinMetadata

"%CSC%" /nologo /target:winexe /optimize /win32icon:DigitControl.ico /out:DigitControl.exe ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
    /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll ^
    /r:"%WINMD%\Windows.Foundation.winmd" /r:"%WINMD%\Windows.Devices.winmd" ^
    DigitControl.cs
if errorlevel 1 (
    if not defined CI pause
    exit /b 1
)

echo Built DigitControl.exe
if not defined CI pause
