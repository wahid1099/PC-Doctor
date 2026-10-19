@echo off
REM Requires the .NET 8 SDK: https://dotnet.microsoft.com/download
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
echo.
echo Done: dist\PCDoctor.exe
pause
