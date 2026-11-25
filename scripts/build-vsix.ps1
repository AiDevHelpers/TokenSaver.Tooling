#Requires -Version 7
<#
.SYNOPSIS
  Builds the WiX MSI for TokenSaver.MCP, copies it into the extension's bin/ folder, then
  packages the VSIX to build/dist/tokensaver.vsix.

.DESCRIPTION
  The VSIX no longer bundles a raw .exe binary — end users (or an AI agent acting on
  INSTALL_PROMPT.md) run the bundled MSI via msiexec (through the extension's
  "Install ... as Windows Service" command, or standalone) to install TokenSaver.MCP onto the
  machine. See extension.ts's installTokenSaverService().

  This script only bundles TokenSaver.Setup's MSI. SimpleModelServer's MSI (built from the
  separate rsperry79/SimpleModelServer repo) is NOT bundled here — if the VSIX needs to ship both,
  that's a separate post-processing step run by whatever orchestrates both repos (see
  F:\mcp's own build-vsix.ps1 for that composition), which copies SimpleModelServer-Setup.msi into
  this VSIX's bin\SimpleModelServer.Setup\ folder after this script has produced the base VSIX.
#>
param(
  [string]$Configuration = "Release",
  [string]$Runtime       = "win-x64",
  [string]$MsiVersion    = "1.0.0",  # version baked into the bundled MSI
  [switch]$NoBuild,      # skip building the MSI, only repackage
  [switch]$NoInstall     # skip uninstall/install-into-VS Code steps (for CI / esproj Publish)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot | Split-Path -Parent
$ExtDir   = Join-Path $RepoRoot "src\Tooling\TokenSaver.VsCodeExt"
$DistDir  = Join-Path $RepoRoot "build\dist"

$null = New-Item -ItemType Directory -Force -Path $DistDir

# ── Uninstall existing extension so VS Code releases file locks ──────────────
# Skipped entirely under -NoInstall (e.g. when this script is invoked from the .esproj's
# PublishCommand) since a `dotnet publish` shouldn't reach out and mutate the developer's
# installed VS Code extensions as a side effect.
if (-not $NoInstall) {
  Write-Host ""
  Write-Host "==> Uninstalling existing TokenSaver extension ..." -ForegroundColor Cyan
  & code --uninstall-extension local.tokensaver-vscode 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "    (not installed or VS Code not in PATH — continuing)" -ForegroundColor Yellow
  } else {
    Write-Host "    Uninstalled." -ForegroundColor Green
  }
}

# ── WiX MSI ──────────────────────────────────────────────────────────────────
# Builds the installer and bundles it into the extension's bin/ folder so extension.ts's
# installTokenSaverService()/uninstallTokenSaverService() can shell out to msiexec against a
# copy that ships inside the VSIX, rather than requiring the user to have already run the
# standalone installer separately. The VSIX does NOT bundle a raw .exe binary — the MSI is
# the only artifact that ships, and it installs the real binary onto the machine (under
# Program Files\TokenSaver). Skippable via -NoBuild for a repackage-only run — this requires
# the WiX .NET tool (dotnet tool install -g wix).
$MsiTarget = @{
  Name         = "TokenSaver.MCP"
  CsProj       = Join-Path $RepoRoot "src\Host\TokenSaver.MCP\TokenSaver.MCP.csproj"
  # Staged on the OS temp drive (NTFS), NOT under build\staging\TokenSaver.MCP: that path is
  # also where Directory.Build.props redirects TokenSaver.MCP.csproj's own normal bin/obj for
  # every ordinary solution build (see root Directory.Build.props). Deleting+republishing into
  # that shared folder here raced with the outer solution build (or with
  # TokenSaver.MCP.Tests's compile against TokenSaver.MCP's ref assembly) and corrupted it —
  # surfaced as CS0006 "Metadata file ...TokenSaver.MCP.dll could not be found" and MSB3030
  # "could not copy ... because it was not found". Matches
  # scripts\publish-tokensaver-for-wix.ps1's $StageDir and TokenSaver.Setup.wixproj's own
  # PublishDir default — same location, same ReFS/cabinet-compression rationale documented
  # there, and it also means the wixproj build below reuses this publish instead of silently
  # republishing to its own default location a second time.
  WixStageDir  = Join-Path $env:TEMP "TokenSaverWixStage"
  WixProj      = Join-Path $RepoRoot "src\Installers\TokenSaver.Setup\TokenSaver.Setup.wixproj"
  MsiBinOut    = Join-Path $ExtDir   "bin\TokenSaver.Setup"
  MsiDistName  = "TokenSaver-Setup-$MsiVersion.msi"
  MsiBinName   = "TokenSaver-Setup.msi"
}

if (-not $NoBuild) {
  Write-Host ""
  Write-Host "==> Building WiX MSI ..." -ForegroundColor Cyan

  # Sanity-check the wix .NET global tool is actually present before starting the (slow) MSI
  # build below — note the actual WiX compilation happens via `dotnet build $MsiTarget.WixProj`
  # further down (WixToolset.Sdk's own MSBuild targets), not by invoking `wix` directly, so
  # this check exists only to fail fast with a clear message rather than a confusing WiX-SDK
  # error deep inside the dotnet build output.
  #
  # Get-Command alone is not fully reliable here: `dotnet tool install -g` adds
  # %USERPROFILE%\.dotnet\tools to the *user* PATH via the registry, but that only takes
  # effect for processes launched after Explorer's environment block refreshes (a full
  # sign-out/sign-in or reboot — NOT just relaunching an app from an already-running
  # explorer.exe/devenv.exe). This bit real: `wix --version` worked fine from an interactive
  # pwsh session while Visual Studio's own Exec task (running this exact script) reported
  # "wix CLI not found on PATH" moments later, because devenv.exe had inherited a stale PATH
  # from before the tool was installed. Also check the known install location directly so a
  # stale process PATH doesn't hard-fail the build with a misleading "not found" — only throw
  # if wix truly isn't installed anywhere.
  $wixOnPath = [bool](Get-Command wix -ErrorAction SilentlyContinue)
  $wixToolPath = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
  $wixAtKnownPath = Test-Path $wixToolPath
  if (-not $wixOnPath -and -not $wixAtKnownPath) {
    throw "wix CLI not found on PATH or at $wixToolPath. Install it with: dotnet tool install -g wix (or pass -NoBuild to skip)."
  }
  if (-not $wixOnPath -and $wixAtKnownPath) {
    Write-Host "    wix not resolvable via PATH in this process (stale environment — sign out/in or reboot to fix permanently)." -ForegroundColor Yellow
    Write-Host "    Found it at $wixToolPath though, so the dotnet build steps below (which don't need PATH) should still work." -ForegroundColor Yellow
  }

  Write-Host ""
  Write-Host "==> Publishing $($MsiTarget.Name) for MSI staging ..." -ForegroundColor Cyan

  # Each WiX project reads from its own build/staging/<Name> dir (see PublishDir in the
  # respective .wixproj) — publish into it directly here, matching how
  # publish-tokensaver-for-wix.ps1 does it standalone.
  if (Test-Path $MsiTarget.WixStageDir) {
    Remove-Item $MsiTarget.WixStageDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $MsiTarget.WixStageDir | Out-Null

  dotnet publish $MsiTarget.CsProj `
    --configuration $Configuration `
    --runtime       $Runtime `
    --self-contained true `
    --output        $MsiTarget.WixStageDir
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for the WiX MSI's staging copy of $($MsiTarget.Name)" }

  Write-Host "==> Building WiX MSI for $($MsiTarget.Name) ..." -ForegroundColor Cyan
  dotnet build $MsiTarget.WixProj -c $Configuration -p:MyAppVersion=$MsiVersion "-p:PublishDir=$($MsiTarget.WixStageDir)"
  if ($LASTEXITCODE -ne 0) { throw "WiX build failed for $($MsiTarget.Name) (exit $LASTEXITCODE)" }

  $MsiPath = Join-Path $RepoRoot "build\dist\$($MsiTarget.MsiDistName)"
  if (-not (Test-Path $MsiPath)) {
    throw "WiX build succeeded but MSI not found at: $MsiPath"
  }

  $null = New-Item -ItemType Directory -Force -Path $MsiTarget.MsiBinOut
  Copy-Item -Path $MsiPath -Destination (Join-Path $MsiTarget.MsiBinOut $MsiTarget.MsiBinName) -Force
  $msiSize = (Get-Item $MsiPath).Length / 1MB
  Write-Host "    $($MsiTarget.MsiBinName)  ($([math]::Round($msiSize, 1)) MB)  -> $($MsiTarget.MsiBinOut)" -ForegroundColor Green
}

Write-Host ""
Write-Host "==> Installing npm dependencies ..." -ForegroundColor Cyan
Push-Location $ExtDir
try {
  npm install
  if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
} finally {
  Pop-Location
}

Write-Host ""
Write-Host "==> Building TypeScript ..." -ForegroundColor Cyan
Push-Location $ExtDir
try {
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
} finally {
  Pop-Location
}

Write-Host ""
Write-Host "==> Packaging VSIX -> build/dist/tokensaver.vsix ..." -ForegroundColor Cyan
$VsixPath = Join-Path $DistDir "tokensaver.vsix"
Push-Location $ExtDir
try {
  npx vsce package --out $VsixPath --no-dependencies
  if ($LASTEXITCODE -ne 0) { throw "vsce package failed" }
} finally {
  Pop-Location
}

# ── Install ───────────────────────────────────────────────────────────────────
if (-not $NoInstall) {
  Write-Host ""
  Write-Host "==> Installing $VsixPath ..." -ForegroundColor Cyan
  & code --install-extension $VsixPath --force 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Host "    Install failed (VS Code not in PATH?). Install manually:" -ForegroundColor Yellow
    Write-Host "      code --install-extension `"$VsixPath`" --force"
  } else {
    Write-Host "    Installed. Reload VS Code window to activate." -ForegroundColor Green
  }
}

Write-Host ""
Write-Host "Done: $VsixPath" -ForegroundColor Green
Write-Host "Note: this VSIX bundles only TokenSaver.Setup.msi. If SimpleModelServer's MSI also" -ForegroundColor Yellow
Write-Host "needs bundling, run the cross-repo composition step from F:\mcp afterward." -ForegroundColor Yellow
