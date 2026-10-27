@echo off
rem Usage: scan.bat <path-to-sln-or-folder> [extra LocalSonar options]
rem Set LOCALSONAR_EXE if LocalSonar.exe is not in C:\Tools\LocalSonar.
setlocal
if "%LOCALSONAR_EXE%"=="" set "LOCALSONAR_EXE=C:\Tools\LocalSonar\LocalSonar.exe"

if "%~1"=="" (
  echo Usage: scan.bat ^<path-to-sln-or-folder^> [--skip-issues] [--skip-duplication]
  exit /b 2
)

"%LOCALSONAR_EXE%" SpecificSln "%~1" %2 %3 %4 %5
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" (echo. & echo Safe to push.) else (echo. & echo Fix the findings above before pushing. [exit code %RC%])
exit /b %RC%
