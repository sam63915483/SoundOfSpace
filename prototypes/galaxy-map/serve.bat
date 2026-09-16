@echo off
REM fetch() will not load galaxy.json from file:// - this has to be served over http.
cd /d "%~dp0"
echo.
echo   GALAXY MAP prototype  ->  http://localhost:8091/
echo   Ctrl+C to stop.
echo.
start "" "http://localhost:8091/"
py -3 -m http.server 8091
