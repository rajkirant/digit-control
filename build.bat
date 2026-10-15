@echo off
rem Builds DigitControl.exe with the C# compiler that ships with Windows
rem (.NET Framework 4), so nothing needs installing.

cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe. Turn on .NET Framework 4 in "Turn Windows features on or off".
    pause
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize /out:DigitControl.exe ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
    DigitControl.cs
if errorlevel 1 (
    pause
    exit /b 1
)

echo Built DigitControl.exe
pause
