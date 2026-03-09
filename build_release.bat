@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "DIST=%ROOT%dist_release"
set "TEMP_PUB=%ROOT%_tmp_plugin_publish"

echo ============================================
echo   CFGS Release Build (win-x64, SingleFile)
echo ============================================
echo.

:: Alten dist_release Ordner bereinigen
if exist "%DIST%" (
    echo Cleaning previous dist_release...
    rmdir /s /q "%DIST%"
)
mkdir "%DIST%"
mkdir "%DIST%\plugins"

echo.
echo [1/2] Publishing CFGS_VM as single-file exe...
echo.

dotnet publish "%ROOT%CFGS_NE\CFGS_VM.csproj" ^
    -c Release ^
    -r win-x64 ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    --self-contained true ^
    -o "%DIST%" ^
    --nologo

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: CFGS_VM publish failed!
    exit /b 1
)

:: Nur die EXE behalten, PDB etc. entfernen
del /q "%DIST%\*.pdb" 2>nul
del /q "%DIST%\*.deps.json" 2>nul
del /q "%DIST%\*.runtimeconfig.json" 2>nul
del /q "%DIST%\*.dll" 2>nul

echo.
echo [2/2] Publishing plugin DLLs (framework-dependent)...
echo.

set PLUGINS=CFGS.StandardLibrary CFGS.Web.Http CFGS.Microsoft.SQL CFGS.Security.Crypto

for %%P in (%PLUGINS%) do (
    echo   Publishing %%P ...

    :: Temp-Ordner bereinigen
    if exist "%TEMP_PUB%" rmdir /s /q "%TEMP_PUB%"

    dotnet publish "%ROOT%%%P\%%P.csproj" ^
        -c Release ^
        -r win-x64 ^
        --self-contained false ^
        -o "%TEMP_PUB%" ^
        --nologo -v q

    if !ERRORLEVEL! neq 0 (
        echo   ERROR: %%P publish failed!
        exit /b 1
    )

    :: Alle DLLs kopieren, AUSSER CFGS_VM selbst
    for %%F in ("%TEMP_PUB%\*.dll") do (
        set "FNAME=%%~nF"
        if /i not "!FNAME!"=="CFGS_VM" (
            copy /y "%%F" "%DIST%\plugins\" >nul
        )
    )

    :: Native DLLs (.so, .dylib) und SNI kopieren falls vorhanden
    for %%F in ("%TEMP_PUB%\*.pdb") do (
        rem PDBs nicht kopieren
    )
)

:: Temp bereinigen
if exist "%TEMP_PUB%" rmdir /s /q "%TEMP_PUB%"

:: deps.json und runtimeconfig aus plugins entfernen
del /q "%DIST%\plugins\*.deps.json" 2>nul
del /q "%DIST%\plugins\*.runtimeconfig.json" 2>nul
del /q "%DIST%\plugins\*.pdb" 2>nul

echo.
echo ============================================
echo   Build complete!
echo ============================================
echo.
echo   Output: %DIST%
echo.
echo   CFGS_VM.exe  (64-bit, self-contained, single-file)
echo.
echo   Plugins:
for %%F in ("%DIST%\plugins\CFGS.*.dll") do (
    echo     %%~nxF
)
echo.

set /a COUNT=0
for %%F in ("%DIST%\plugins\*.dll") do set /a COUNT+=1
echo   Total: !COUNT! DLLs in plugins\
echo.

endlocal
