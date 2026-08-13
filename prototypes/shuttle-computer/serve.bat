@echo off
REM ES modules will not load from file:// — this has to be served over http.
cd /d "%~dp0"
echo.
echo   Shuttle computer prototype  ->  http://localhost:8080/
echo   Ctrl+C to stop.
echo.
start "" "http://localhost:8080/"
python -m http.server 8080
