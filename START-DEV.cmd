@echo off
rem ============================================================
rem  Emergency Archive - development launcher
rem  Runs the UI from source against the vault on D:\vault
rem  (one-time setup: dotnet run --project tools\VaultCli -- create D:\vault)
rem ============================================================
set "EMERGENCY_ARCHIVE_VAULT_PATH=D:\vault"
set "PATH=C:\Users\mothe\AppData\Local\Microsoft\dotnet;%PATH%"
cd /d C:\Users\mothe\projects\secure_drive
"C:\Users\mothe\AppData\Local\Microsoft\dotnet\dotnet.exe" run --project src\EmergencyArchive.UI -c Debug
pause
