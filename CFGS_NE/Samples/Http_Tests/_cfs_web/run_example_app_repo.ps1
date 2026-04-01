$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\..\..\.."))
$distDir = Join-Path $repoRoot "dist\Debug\net10.0"
$pluginsDir = Join-Path $distDir "plugins"
$scriptPath = Join-Path $scriptDir "run_example_app.cfs"

if (!(Test-Path $distDir)) {
    throw "Repo build not found: $distDir"
}

New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

$pluginNames = @(
    "CFGS.StandardLibrary.dll",
    "CFGS.Security.Crypto.dll",
    "CFGS.Web.Http.dll"
)

foreach ($pluginName in $pluginNames) {
    $src = Join-Path $distDir $pluginName
    $dst = Join-Path $pluginsDir $pluginName

    if (!(Test-Path $src)) {
        throw "Missing built plugin: $src"
    }

    Copy-Item -LiteralPath $src -Destination $dst -Force
}

Push-Location $distDir
try {
    & dotnet .\CFGS_VM.dll $scriptPath
}
finally {
    Pop-Location
}
