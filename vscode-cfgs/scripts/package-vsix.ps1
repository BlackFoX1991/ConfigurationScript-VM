param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$extensionRoot = Join-Path $repoRoot "vscode-cfgs"
$distRoot = Join-Path $repoRoot "dist\$Configuration\net10.0"
$packageJsonPath = Join-Path $extensionRoot "package.json"

if (-not (Test-Path $packageJsonPath)) {
    throw "package.json not found at $packageJsonPath"
}

if (-not (Test-Path (Join-Path $distRoot "CFGS.Lsp.dll"))) {
    throw "Bundled LSP server not found. Build CFGS.Lsp first into $distRoot."
}

$package = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$publisher = [string]$package.publisher
$name = [string]$package.name
$version = [string]$package.version
$displayName = [string]$package.displayName
$description = [string]$package.description
$engineVersion = [string]$package.engines.vscode
$minVersion = $engineVersion.TrimStart('^')
$vsixName = "$publisher.$name-$version.vsix"
$outputDir = Join-Path $repoRoot "artifacts"
$stagingDir = Join-Path $repoRoot "_tmp_vsix"
$extensionStage = Join-Path $stagingDir "extension"
$serverStage = Join-Path $extensionStage "server"
$vsixPath = Join-Path $outputDir $vsixName
$zipPath = Join-Path $outputDir "$vsixName.zip"

if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}

New-Item -ItemType Directory -Path $extensionStage -Force | Out-Null
New-Item -ItemType Directory -Path $serverStage -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$extensionFiles = @(
    "extension.js",
    "language-configuration.json",
    "package.json",
    "README.md"
)

foreach ($relativePath in $extensionFiles) {
    Copy-Item (Join-Path $extensionRoot $relativePath) (Join-Path $extensionStage $relativePath)
}

Copy-Item (Join-Path $extensionRoot "syntaxes") (Join-Path $extensionStage "syntaxes") -Recurse

$serverFiles = @(
    "CFGS.Lsp.dll",
    "CFGS.Lsp.deps.json",
    "CFGS.Lsp.runtimeconfig.json",
    "CFGS_VM.dll",
    "CFGS_VM.deps.json"
)

foreach ($relativePath in $serverFiles) {
    Copy-Item (Join-Path $distRoot $relativePath) (Join-Path $serverStage $relativePath)
}

$escapedDisplayName = [System.Security.SecurityElement]::Escape($displayName)
$escapedDescription = [System.Security.SecurityElement]::Escape($description)
$escapedIdentity = [System.Security.SecurityElement]::Escape("$publisher.$name")

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Id="$escapedIdentity" Version="$version" Language="en-US" Publisher="$publisher" />
    <DisplayName>$escapedDisplayName</DisplayName>
    <Description xml:space="preserve">$escapedDescription</Description>
    <Categories>Programming Languages</Categories>
    <Tags>cfgs configurationscript language-server</Tags>
    <GalleryFlags>Public</GalleryFlags>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$engineVersion" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionDependencies" Value="" />
    </Properties>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" Version="[$minVersion,2.0.0)" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
    <Asset Type="Microsoft.VisualStudio.Services.Content.Details" Path="extension/README.md" Addressable="true" />
  </Assets>
</PackageManifest>
"@

$contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="js" ContentType="application/javascript" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="xml" ContentType="application/xml" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="vsixmanifest" ContentType="text/xml" />
</Types>
"@

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $stagingDir "extension.vsixmanifest"), $manifest, $utf8)
[System.IO.File]::WriteAllText((Join-Path $stagingDir "[Content_Types].xml"), $contentTypes, $utf8)

if (Test-Path $vsixPath) {
    Remove-Item -Force $vsixPath
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force
Move-Item -Path $zipPath -Destination $vsixPath

Write-Output $vsixPath
