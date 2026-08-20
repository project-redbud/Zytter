@echo off
rem Open TWO Zytter client windows at once (server must be running).
rem Window 1 plays BGM/sound; window 2 is launched with --nobgm to avoid overlapping audio.
rem Log in with two DIFFERENT accounts in the two windows, then both press "match".

set GODOT=Godot_v4.7-stable_mono_win64.exe
set PROJECT=src\Zytter.Client

if not exist "%GODOT%" (
    echo Godot not found at %GODOT% - edit this file to fix the path.
    pause
    exit /b 1
)

start "" "%GODOT%" --path "%PROJECT%"
ping -n 3 127.0.0.1 >nul
start "" "%GODOT%" --path "%PROJECT%" -- --nobgm
echo Two client windows launched. Window 2 has BGM disabled. Server: http://127.0.0.1:17717
