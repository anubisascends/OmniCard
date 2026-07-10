@echo off
REM Sync test data from external sources into TestData/
REM Usage: sync-test-data.bat [--scryfall-db path] [--art-dir path] [--scan-source path]
REM Defaults: scryfall-db = X:\TCG Card Scanner\scryfall.db
REM           art-dir     = X:\TCG Card Scanner
REM           scan-source = C:\Users\anubi\OneDrive\Desktop\Test_Data
dotnet run --project "%~dp0..\Tools\SyncTestData\SyncTestData.csproj" -- %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Sync failed with error code %ERRORLEVEL%
    pause
) else (
    echo.
    echo Done! You can now run the integration tests.
)
