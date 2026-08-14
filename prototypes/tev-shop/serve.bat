@echo off
REM Tev's shop mockups. Static, but served over http to match the other
REM prototypes (and so the browser does not treat it as a local file).
cd /d "%~dp0"
echo.
echo   Tev's shop mockups  ->  http://localhost:8082/
echo   Ctrl+C to stop.
echo.
start "" "http://localhost:8082/"
python -m http.server 8082
