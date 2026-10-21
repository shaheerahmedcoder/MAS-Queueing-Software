@echo off
cd /d "%~dp0"
dotnet run --project QueueingSuite.App -c Release
