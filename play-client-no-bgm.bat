@echo off
rem Launch one Zytter client window.
rem   play-client.bat            -> normal client with BGM
rem   play-client.bat nobgm      -> client without BGM (use for the second player)
rem The two clients MUST log in with two DIFFERENT accounts.

set GODOT=Godot_v4.7-stable_mono_win64.exe
set PROJECT=src\Zytter.Client

if not exist "%GODOT%" (
    echo Godot not found at %GODOT% - edit this file to fix the path.
    pause
    exit /b 1
)

start "" "%GODOT%" --path "%PROJECT%" -- --nobgm
