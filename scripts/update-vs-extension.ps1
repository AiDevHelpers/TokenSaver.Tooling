#Requires -Version 7
<#
.SYNOPSIS
  Rebuilds the TokenSaver VS extension and installs it over the existing one (in-place upgrade).

.DESCRIPTION
  Rebuilds the extension from source, then opens the resulting .vsix by path (its own file
  association) so VsixInstaller.exe runs interactively and upgrades the installed extension. The
  upgrade works because the version increases with each commit. Pass -Uninstall to remove the old
  extension first (stable AND Insiders/Preview both register under the same product IDs, see
  source.extension.vsixmanifest's InstallationTarget); use it only if an in-place upgrade fails.

  VsixInstaller.exe runs WITHOUT /quiet, on purpose: VsixInstaller needs to elevate (UAC)
  to write into Program Files, and every attempt at driving it non-interactively (/quiet,
  /uninstall:<id> under a hidden window, or any other argument combination run non-interactively)
  either hung indefinitely, threw exit 2001 (InvalidCommandLineException), or reported the package
  as corrupt. All three turned out to be the same root cause: a non-interactive session can't see
  or click the elevation prompt, so the process blocks forever waiting on it (and can leave an
  unkillable elevated process tree behind — Stop-Process/taskkill both fail with "Access is denied"
  against it). Running VsixInstaller visibly and letting you click through each prompt is the only
  invocation that has actually worked — so expect a UAC prompt and a small VsixInstaller dialog per
  instance, for both the uninstall and the install.

  IMPORTANT — commit (and push) before running this: Nerdbank.GitVersioning derives the extension's
  version from git height (commit count), not from file contents. If you rebuild without a new
  commit, the VSIX comes out at the exact same version already installed, and VsixInstaller treats
  a same-version reinstall as a no-op ("Modify") that does NOT pick up your code changes — see
  ExtensionEntrypoint.cs's remarks on why AssemblyVersion is pinned to track git height for exactly
  this reason. Uncommitted working-tree changes do not bump the height at all.
#>
param(
  [string]$Configuration = "Release",
  [switch]$NoBuild,         # skip building, only reinstall the existing VSIX
  [switch]$Uninstall,       # uninstall the old extension first instead of upgrading in place
  [switch]$IncludePreview   # also offer the Insiders/Preview instance; by default only stable Visual Studio is updated
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot | Split-Path -Parent
$DistDir = Join-Path $RepoRoot "build\dist"
$VsixPath = Join-Path $DistDir "TokenSaver.Vs.Extension.vsix"
$ExtensionIdentifier = "TokenSaver.Vs.Extension"

Write-Host ""
Write-Host "==> TokenSaver VS Extension Update" -ForegroundColor Cyan

# ── Discover every matching VS instance (stable + Insiders/Preview) ───────────
# Hardcoded rather than built from $env:ProgramFiles(x86): that env var isn't reliably present in
# every PowerShell host (e.g. it's missing entirely under some remote/non-interactive sessions),
# and vswhere.exe always installs to this fixed path regardless of Visual Studio edition/channel.
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$instances = @()
if (Test-Path $vswhere) {
  $instances = & $vswhere -all -prerelease -format json |
    ConvertFrom-Json |
    Where-Object { [version]($_.installationVersion -replace '-.*$') -ge [version]"18.0" }
}

if (-not $instances) {
  Write-Host ""
  Write-Host "==> No VS 2026 (18.x) instances found via vswhere" -ForegroundColor Yellow
} else {
  Write-Host ""
  Write-Host "==> Found $($instances.Count) matching VS instance(s):" -ForegroundColor Cyan
  foreach ($inst in $instances) {
    Write-Host "    - $($inst.displayName) $($inst.catalog.productDisplayVersion) [$($inst.channelId)]" -ForegroundColor Gray
  }
}

# ── Uninstall old extension from every discovered instance (opt-in, interactive) ─
if ($Uninstall) {
  Write-Host ""
  Write-Host "==> Uninstalling old TokenSaver VS Extension ..." -ForegroundColor Yellow
  Write-Host "    A VsixInstaller window (and possibly a UAC prompt) will appear per instance -" -ForegroundColor Gray
  Write-Host "    approve/click through each one; the script waits for it to close." -ForegroundColor Gray

  # The extension is registered per user, so one /uninstall removes it from every instance.
  # Running it once per instance makes the second run fail with NotInstalledException.
  $uninstallOnce = $instances | Where-Object {
    Test-Path (Join-Path $_.installationPath "Common7\IDE\VsixInstaller.exe")
  } | Select-Object -First 1

  if (-not $uninstallOnce) {
    Write-Host "    No VsixInstaller.exe found, nothing to uninstall" -ForegroundColor Yellow
  } else {
    $vsixInstaller = Join-Path $uninstallOnce.installationPath "Common7\IDE\VsixInstaller.exe"
    Write-Host "    $($uninstallOnce.displayName) [$($uninstallOnce.channelId)]: waiting on uninstall dialog ..." -ForegroundColor Gray
    Start-Process -FilePath $vsixInstaller -ArgumentList "/uninstall:$ExtensionIdentifier" -Wait
    Write-Host "    uninstall dialog closed" -ForegroundColor Green
  }
}

# The version is derived from git height (Nerdbank.GitVersioning) - uncommitted changes don't move
# it, so a rebuild here would produce the exact VSIX already installed and VsixInstaller would just
# no-op the "Modify" instead of updating anything. Commit (and push) first.
if (-not $NoBuild) {
  $gitStatus = git -C $RepoRoot status --porcelain 2>$null
  if ($gitStatus) {
    Write-Host ""
    Write-Host "==> Uncommitted changes detected - the VSIX version won't change until you commit." -ForegroundColor Yellow
    Write-Host "    Commit (and push) first, or the reinstall will be a same-version no-op." -ForegroundColor Yellow
  }
}

# ── Build the new VSIX ───────────────────────────────────────────────────────
if (-not $NoBuild) {
  Write-Host ""
  Write-Host "==> Building new VSIX ..." -ForegroundColor Cyan
  & (Join-Path $RepoRoot "scripts\build-vs-extension-vsix.ps1") -Configuration $Configuration -NoInstall
  if ($LASTEXITCODE -ne 0) {
    throw "VSIX build failed"
  }
}

if (-not (Test-Path $VsixPath)) {
  throw "VSIX not found at: $VsixPath"
}

# ── Install the new VSIX (interactive) ────────────────────────────────────────
Write-Host ""
$stable = @($instances | Where-Object {
  -not $_.isPrerelease -and (Test-Path (Join-Path $_.installationPath "Common7\IDE\VsixInstaller.exe"))
})

if ($IncludePreview -or $stable.Count -eq 0) {
  # Opening the .vsix by its file association makes VsixInstaller list every matching instance.
  Write-Host "==> Opening $VsixPath ..." -ForegroundColor Cyan
  Write-Host "    VsixInstaller will list every matching VS instance (stable + Insiders) -" -ForegroundColor Gray
  Write-Host "    pick the ones to update, and approve the elevation (UAC) prompt." -ForegroundColor Gray
  Start-Process -FilePath $VsixPath -Wait
} else {
  # Name the instance, so the Insiders/Preview install is left alone.
  foreach ($inst in $stable) {
    $vsixInstaller = Join-Path $inst.installationPath "Common7\IDE\VsixInstaller.exe"
    Write-Host "==> Installing into $($inst.displayName) $($inst.catalog.productDisplayVersion) only ..." -ForegroundColor Cyan
    Write-Host "    Approve the elevation (UAC) prompt; the script waits for the installer to close." -ForegroundColor Gray
    Start-Process -FilePath $vsixInstaller -ArgumentList @("/instanceIds:$($inst.instanceId)", "`"$VsixPath`"") -Wait
  }
}

Write-Host ""
Write-Host "Done! Reload Visual Studio to activate the new extension." -ForegroundColor Green
