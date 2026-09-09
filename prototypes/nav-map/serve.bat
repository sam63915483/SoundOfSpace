@echo off
REM fetch() will not load system.json from file:// - this has to be served over http.
cd /d "%~dp0"
echo.
echo   NAV solar map prototype  ->  http://localhost:8090/
echo   Ctrl+C to stop.
echo.
start "" "http://localhost:8090/"
python -m http.server 8090
