#Requires -Version 7
<#
.SYNOPSIS
    Associates the .vsix file extension with VS Code instead of Visual Studio, so
    double-clicking a .vsix (e.g. build\dist\tokensaver.vsix) installs it into VS Code
    via `code --install-extension` rather than launching Visual Studio's VSIX installer.

.DESCRIPTION
    Visual Studio registers a ProgID for .vsix (VisualStudio.Vsix or similar) at install
    time, which wins the file association by default since VS Code does not register a
    .vsix handler of its own. This script creates a dedicated ProgID
    (VSCode.vsix.tokensaver) under HKCU\Software\Classes, points its default "open" verb
    at "code.exe" --install-extension "%1", and sets it as the user's default handler for
    .vsix via HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.vsix\UserChoice.

    Only touches HKCU (current user), not HKLM — no admin rights required, and it does not
    affect Visual Studio's own File > Open Project handling of .vsix, only Explorer's
    double-click behavior.

    NOTE: Windows may silently ignore a scripted UserChoice write (it's hash-protected on
    Windows 10 1903+ to force association changes through Settings UI). If double-click
    still opens Visual Studio after running this script, the fallback -OpenSettings switch
    launches ms-settings so you can pick VS Code manually, or you can always run:
        code --install-extension "build\dist\tokensaver.vsix"
    directly instead of double-clicking.

.PARAMETER CodeExePath
    Path to VS Code's code.exe. Auto-detected from PATH / common install locations if
    omitted.

.PARAMETER OpenSettings
    After attempting the registry association, open Windows Settings' "Choose default
    apps by file type" page filtered to .vsix, in case the scripted UserChoice write was
    ignored (common on Windows 10/11 due to hash protection).

.EXAMPLE
    .\associate-vsix-vscode.ps1
    .\associate-vsix-vscode.ps1 -OpenSettings
    .\associate-vsix-vscode.ps1 -CodeExePath "C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe"
#>
[CmdletBinding()]
param(
    [string]$CodeExePath,
    [switch]$OpenSettings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate code.exe ───────────────────────────────────────────────────────────
if (-not $CodeExePath) {
    $cmd = Get-Command code.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        $CodeExePath = $cmd.Source
    } else {
        $candidates = @(
            "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
            "$env:ProgramFiles\Microsoft VS Code\Code.exe",
            "${env:ProgramFiles(x86)}\Microsoft VS Code\Code.exe"
        )
        $CodeExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}

if (-not $CodeExePath -or -not (Test-Path $CodeExePath)) {
    throw "Could not locate code.exe. Pass -CodeExePath explicitly, e.g.:`n  .\associate-vsix-vscode.ps1 -CodeExePath 'C:\Users\<you>\AppData\Local\Programs\Microsoft VS Code\Code.exe'"
}

Write-Host "Using VS Code at: $CodeExePath" -ForegroundColor Cyan

# ── Register a ProgID for .vsix -> VS Code ────────────────────────────────────
$progId = "VSCode.vsix.tokensaver"
$progIdKey = "HKCU:\Software\Classes\$progId"

Write-Host "Creating ProgID $progId ..." -ForegroundColor Cyan
New-Item -Path $progIdKey -Force | Out-Null
Set-ItemProperty -Path $progIdKey -Name "(Default)" -Value "VS Code Extension Package"

$iconKey = "$progIdKey\DefaultIcon"
New-Item -Path $iconKey -Force | Out-Null
Set-ItemProperty -Path $iconKey -Name "(Default)" -Value "`"$CodeExePath`",0"

$commandKey = "$progIdKey\shell\open\command"
New-Item -Path $commandKey -Force | Out-Null
Set-ItemProperty -Path $commandKey -Name "(Default)" -Value "`"$CodeExePath`" --install-extension `"%1`""

# ── Register the .vsix extension itself (OpenWithProgids) ────────────────────
$extKey = "HKCU:\Software\Classes\.vsix"
New-Item -Path $extKey -Force | Out-Null

$openWithKey = "$extKey\OpenWithProgids"
New-Item -Path $openWithKey -Force | Out-Null
New-ItemProperty -Path $openWithKey -Name $progId -Value "" -PropertyType String -Force | Out-Null

Write-Host "Registered $progId as an available handler for .vsix." -ForegroundColor Green

# ── Attempt to set as default via UserChoice ──────────────────────────────────
# Windows 10 1903+ / Windows 11 hash-protects UserChoice writes so unsigned scripts can't
# silently hijack file associations. This write frequently gets ignored by Explorer even
# though it succeeds at the registry level — hence the -OpenSettings fallback below.
try {
    $userChoiceKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.vsix\UserChoice"
    if (Test-Path $userChoiceKey) {
        Remove-Item -Path $userChoiceKey -Force -ErrorAction SilentlyContinue
    }
    New-Item -Path $userChoiceKey -Force | Out-Null
    Set-ItemProperty -Path $userChoiceKey -Name "ProgId" -Value $progId
    Write-Host "Set UserChoice default for .vsix -> $progId (may be ignored by Explorer)." -ForegroundColor Yellow
} catch {
    Write-Host "Could not write UserChoice directly (expected on some Windows builds): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Test with: double-click build\dist\tokensaver.vsix" -ForegroundColor Cyan
Write-Host "If it still opens Visual Studio, go to:" -ForegroundColor Yellow
Write-Host "  Settings > Apps > Default Apps > choose default for '.vsix' > pick 'VS Code Extension Package'" -ForegroundColor Yellow
Write-Host "or just run:  code --install-extension `"build\dist\tokensaver.vsix`"" -ForegroundColor Yellow

if ($OpenSettings) {
    Start-Process "ms-settings:defaultapps"
}
