@echo off
setlocal
set ROOT=%~dp0
set BACKEND=%ROOT%BettingAnalysis
set FRONTEND=%ROOT%Frontend

echo ============================================================
echo  Betting Analysis — Dev Launcher
echo ============================================================

:: ── 1. Kill any stale processes ──────────────────────────────
echo [1/4] Stopping old processes...
taskkill /F /IM BettingAnalysis.exe >nul 2>&1
:: Kill whatever is sitting on port 5100 (backend) and 5173 (frontend)
for /f "tokens=5" %%p in ('netstat -aon ^| findstr ":5100 " 2^>nul') do (
    taskkill /F /PID %%p >nul 2>&1
)
for /f "tokens=5" %%p in ('netstat -aon ^| findstr ":5173 " 2^>nul') do (
    taskkill /F /PID %%p >nul 2>&1
)
timeout /t 1 /nobreak >nul
echo    Done.

:: ── 2. Build backend ─────────────────────────────────────────
echo [2/4] Building backend...
cd /d "%BACKEND%"
dotnet build --configuration Debug --nologo -v quiet
if %ERRORLEVEL% neq 0 (
    echo    BUILD FAILED. Fix errors above then re-run this script.
    pause
    exit /b 1
)
echo    Build OK.

:: ── 3. Install frontend deps (only if node_modules missing) ──
echo [3/4] Checking frontend dependencies...
cd /d "%FRONTEND%"
if not exist "node_modules\" (
    echo    node_modules not found — running npm install...
    npm install
    if %ERRORLEVEL% neq 0 (
        echo    npm install FAILED.
        pause
        exit /b 1
    )
) else (
    echo    node_modules OK.
)

:: ── 4. Launch both in separate windows ───────────────────────
echo [4/4] Starting servers...

start "Backend  :5100" cmd /k "cd /d "%BACKEND%" && dotnet run --no-build --configuration Debug"
timeout /t 2 /nobreak >nul
start "Frontend :5173" cmd /k "cd /d "%FRONTEND%" && npm run dev"

echo.
echo ============================================================
echo  Backend  (Swagger) : http://localhost:5100/swagger
echo  Frontend (UI)      : http://localhost:5173
echo ============================================================
echo  Close the two terminal windows to stop both servers.
echo ============================================================
