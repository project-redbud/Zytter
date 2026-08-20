@echo off
rem Repair the Godot client project after the editor overwrites Zytter.Client.csproj.
rem Close the Godot editor completely BEFORE running this file.

dotnet restore src\Zytter.Client --nologo
if errorlevel 1 goto fail
dotnet build src\Zytter.Client --no-restore --nologo
if errorlevel 1 goto fail
echo.
echo Done. Reopen the Godot editor and press F5.
echo Do NOT click "Create C# Solution" in the editor - it overwrites the csproj.
pause
exit /b 0

:fail
echo.
echo Build failed - see errors above.
pause
exit /b 1
