#Requires -Version 7
<#
.SYNOPSIS
  Builds the TokenSaver Visual Studio extension and packages it into a VSIX.

.DESCRIPTION
  Publishes the TokenSaver.Vs.Extension project and creates a VSIX using the
  Visual Studio SDK's built-in VSIX packaging.

  Output: build/dist/TokenSaver.Vs.Extension.vsix
#>
param(
  [string]$Configuration = "Release",
  [switch]$NoInstall     # skip install-into-VS steps (for CI)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot | Split-Path -Parent
$ExtDir   = Join-Path $RepoRoot "src\Tooling\TokenSaver.Vs.Extension"
$DistDir  = Join-Path $RepoRoot "build\dist"
$ProjFile = Join-Path $ExtDir "TokenSaver.Vs.Extension.csproj"

$null = New-Item -ItemType Directory -Force -Path $DistDir

Write-Host ""
Write-Host "==> Building Visual Studio Extension ..." -ForegroundColor Cyan

# Build the VSIX - the SDK generates it automatically
dotnet build $ProjFile `
  --configuration $Configuration `
  --no-restore
if ($LASTEXITCODE -ne 0) {
  throw "dotnet build failed for Visual Studio extension"
}

# The VSIX output location depends on the build configuration. It lands one directory deeper than
# bin\$Configuration\ - under the TFM folder (net8.0-windows) - so this must recurse.
$VsixSearchRoot = Join-Path $RepoRoot "build\staging\TokenSaver.Vs.Extension\bin\$Configuration"
$VsixPath = Join-Path $DistDir "TokenSaver.Vs.Extension.vsix"

# Copy the generated VSIX to dist
# Take the newest VSIX: staging keeps one per target framework (net8.0-windows, net10.0-windows), and
# the first match by directory order can be a stale build from an earlier target.
$generatedVsix = Get-ChildItem -Path $VsixSearchRoot -Filter "*.vsix" -Recurse -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($generatedVsix) {
  Copy-Item -Path $generatedVsix.FullName -Destination $VsixPath -Force
  Write-Host "    VSIX generated: $VsixPath" -ForegroundColor Green
} else {
  # No fallback: a hand-zipped DLL bundle lacks the VSIX manifest and is rejected by Visual Studio.
  throw "dotnet build did not produce a .vsix under $VsixSearchRoot. Check the build output for VSIX packaging errors (the Microsoft.VisualStudio.Extensibility.Build targets must run)."
}

if (-not (Test-Path $VsixPath)) {
  throw "VSIX not found at: $VsixPath"
}

# Stamp the manifest identity version from Nerdbank (git height). The source manifest keeps the
# $version$ placeholder; without a new version per commit, VsixInstaller treats the rebuild as the
# already-installed extension and does not upgrade it.
# nbgv is a local dotnet tool (.config/dotnet-tools.json), so it is not always on PATH: use `dotnet nbgv` then.
Push-Location $RepoRoot
try {
  if (Get-Command nbgv -ErrorAction SilentlyContinue) {
    $simpleVersion = (& nbgv get-version -v SimpleVersion 2>$null | Select-Object -First 1)
  } else {
    $simpleVersion = (& dotnet nbgv get-version -v SimpleVersion 2>$null | Select-Object -First 1)
  }
  $nbgvExit = $LASTEXITCODE
} finally {
  Pop-Location
}
if ($nbgvExit -ne 0 -or [string]::IsNullOrWhiteSpace($simpleVersion)) {
  throw "nbgv could not compute the extension version (run 'dotnet tool restore' in the repo root)"
}
$manifestVersion = "$($simpleVersion.Trim()).0"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($VsixPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
  $entry = $zip.GetEntry("extension.vsixmanifest")
  if (-not $entry) {
    throw "extension.vsixmanifest not found inside $VsixPath"
  }
  $reader = [System.IO.StreamReader]::new($entry.Open())
  $manifest = $reader.ReadToEnd()
  $reader.Dispose()
  # The generated manifest carries the SDK's placeholder version (1.0.0) in the Identity element.
  $stamped = [regex]::Replace($manifest, '(<Identity Id="TokenSaver\.Vs\.Extension" Version=")[^"]*(")', "`${1}$manifestVersion`$2")
  # The SDK still writes the .NET declaration as net8.0 even when the project targets net10.0; Visual Studio
  # shows an end-of-life notice from it, so correct the declaration to match the target.
  $stamped = $stamped.Replace('<DotnetTargetVersions>net8.0</DotnetTargetVersions>', '<DotnetTargetVersions>net10.0</DotnetTargetVersions>')
  if ($stamped -eq $manifest -and $manifest -notmatch [regex]::Escape("Version=`"$manifestVersion`"")) {
    throw "extension.vsixmanifest has no TokenSaver.Vs.Extension Identity version to stamp"
  }
  $entry.Delete()
  $newEntry = $zip.CreateEntry("extension.vsixmanifest")
  $writer = [System.IO.StreamWriter]::new($newEntry.Open(), [System.Text.UTF8Encoding]::new($false))
  $writer.Write($stamped)
  $writer.Dispose()

  # catalog.json pins the extension's own version in the component dependency; VsixInstaller rejects a
  # mismatch between the catalog and the vsixmanifest.
  $catalogEntry = $zip.GetEntry("catalog.json")
  if (-not $catalogEntry) {
    throw "catalog.json not found inside $VsixPath"
  }
  $catalogReader = [System.IO.StreamReader]::new($catalogEntry.Open())
  $catalog = $catalogReader.ReadToEnd()
  $catalogReader.Dispose()
  # The generated catalog carries the SDK placeholder 1.0.0 in the dependency, each package entry and the info id.
  $stampedCatalog = [regex]::Replace($catalog, '("TokenSaver\.Vs\.Extension":")1\.0\.0(")', "`${1}$manifestVersion`$2")
  $stampedCatalog = [regex]::Replace($stampedCatalog, '("id":"(?:Component\.)?TokenSaver\.Vs\.Extension","version":")1\.0\.0(")', "`${1}$manifestVersion`$2")
  $stampedCatalog = [regex]::Replace($stampedCatalog, '(TokenSaver\.Vs\.Extension,version=)1\.0\.0', "`${1}$manifestVersion")
  if ($stampedCatalog -eq $catalog) {
    throw "catalog.json has no TokenSaver.Vs.Extension version placeholder to stamp"
  }
  $catalogEntry.Delete()
  $newCatalog = $zip.CreateEntry("catalog.json")
  $catalogWriter = [System.IO.StreamWriter]::new($newCatalog.Open(), [System.Text.UTF8Encoding]::new($false))
  $catalogWriter.Write($stampedCatalog)
  $catalogWriter.Dispose()

  # manifest.json is the package manifest; its root "version" is the other value VsixInstaller compares.
  $packageEntry = $zip.GetEntry("manifest.json")
  if (-not $packageEntry) {
    throw "manifest.json not found inside $VsixPath"
  }
  $packageReader = [System.IO.StreamReader]::new($packageEntry.Open())
  $packageManifest = $packageReader.ReadToEnd()
  $packageReader.Dispose()
  $stampedPackage = [regex]::Replace($packageManifest, '("id":"TokenSaver\.Vs\.Extension","version":")1\.0\.0(")', "`${1}$manifestVersion`$2")
  if ($stampedPackage -eq $packageManifest) {
    throw "manifest.json has no TokenSaver.Vs.Extension version placeholder to stamp"
  }
  $packageEntry.Delete()
  $newPackage = $zip.CreateEntry("manifest.json")
  $packageWriter = [System.IO.StreamWriter]::new($newPackage.Open(), [System.Text.UTF8Encoding]::new($false))
  $packageWriter.Write($stampedPackage)
  $packageWriter.Dispose()

  # System.ServiceProcess.ServiceController ships a stub at the package's lib\ (every method throws
  # PlatformNotSupportedException) and the real Windows implementation under runtimes\win\. The out-of-process
  # extension host loads the copy at the VSIX root and ignores runtimes\, so the stub is what ran and the Services
  # tab could not read any service state. Put the Windows implementation at the root.
  $controller = "System.ServiceProcess.ServiceController.dll"
  $winController = $zip.GetEntry("runtimes/win/lib/net8.0/$controller")
  if (-not $winController) {
    throw "runtimes/win/lib/net8.0/$controller not found inside $VsixPath"
  }
  $winStream = $winController.Open()
  $controllerBytes = [System.IO.MemoryStream]::new()
  $winStream.CopyTo($controllerBytes)
  $winStream.Dispose()
  $zip.GetEntry($controller)?.Delete()
  $rootController = $zip.CreateEntry($controller)
  $rootStream = $rootController.Open()
  $rootStream.Write($controllerBytes.ToArray(), 0, [int]$controllerBytes.Length)
  $rootStream.Dispose()
} finally {
  $zip.Dispose()
}
Write-Host "    manifest version: $manifestVersion" -ForegroundColor Green

$vsixSize = (Get-Item $VsixPath).Length / 1MB
Write-Host "    Size: $([math]::Round($vsixSize, 1)) MB" -ForegroundColor Green

Write-Host ""
Write-Host "Done: $VsixPath" -ForegroundColor Green
