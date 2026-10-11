@echo off
rem Collects WorldBoxMP and game logs into one zip on the desktop.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect_logs.ps1"
