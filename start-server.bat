@echo off
rem Launch one Zytter client window.
rem   play-client.bat            -> normal client with BGM
rem   play-client.bat nobgm      -> client without BGM (use for the second player)
rem The two clients MUST log in with two DIFFERENT accounts.

cd src\Zytter.Server
dotnet build
cd bin\Debug\net8.0
Zytter.Server.exe
