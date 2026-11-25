<#
.SYNOPSIS
    Relocates src\Tooling\TokenSaver.VsCodeExt\node_modules to build\node_modules, leaving a
    directory junction behind so npm/tsc/VS Code still find it in the normal place.

.DESCRIPTION
    npm has no config knob for "put node_modules somewhere else" — Node's module resolution
    algorithm requires node_modules to live in an ancestor directory of the file doing the
    require/import. So this isn't a simple config move like the .NET bin/obj redirect: the
    real folder moves to build\node_modules, and a directory junction named "node_modules" is
    left at src\Tooling\TokenSaver.VsCodeExt\ pointing to it. Node, npm, tsc, and a future
    `npm install` all follow the junction transparently.

    Run this from a normal Windows shell (PowerShell) where you have full file permissions —
    it won't work from a sandboxed/remote session.

.PARAMETER WhatIf
    Preview what would happen without actually moving/linking anything.

.EXAMPLE
    .\scripts\move-node-modules.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src\Tooling\TokenSaver.VsCodeExt'
$currentNodeModules = Join-Path $projectDir 'node_modules'
$targetDir = Join-Path $repoRoot 'build\node_modules'

if (-not (Test-Path $currentNodeModules)) {
    Write-Host "No node_modules found at $currentNodeModules — nothing to do." -ForegroundColor Yellow
    return
}

$existingItem = Get-Item -LiteralPath $currentNodeModules -Force
if ($existingItem.LinkType -eq 'Junction') {
    Write-Host "node_modules is already a junction (-> $($existingItem.Target)) — nothing to do." -ForegroundColor Yellow
    return
}

if (Test-Path $targetDir) {
    throw "build\node_modules already exists at $targetDir — remove it first or resolve manually before re-running."
}

if ($PSCmdlet.ShouldProcess($currentNodeModules, "Move to $targetDir and replace with a junction")) {
    Write-Host "Ensuring $repoRoot\build exists ..."
    New-Item -ItemType Directory -Path (Join-Path $repoRoot 'build') -Force | Out-Null

    Write-Host "Moving $currentNodeModules -> $targetDir ..."
    Move-Item -LiteralPath $currentNodeModules -Destination $targetDir

    Write-Host "Creating junction $currentNodeModules -> $targetDir ..."
    New-Item -ItemType Junction -Path $currentNodeModules -Target $targetDir | Out-Null

    Write-Host "Done. node_modules now lives at $targetDir, linked from $currentNodeModules." -ForegroundColor Green
}
