@echo off
dotnet build "%~dp0BottleRefiller.csproj" -c Release --nologo
if errorlevel 1 pause
