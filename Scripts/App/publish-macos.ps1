<#
.SYNOPSIS
    Builds the macOS package of RCynQ from Windows, inside WSL.

.DESCRIPTION
    Runs Scripts/App/publish-in-wsl.sh in a WSL distribution for a macOS runtime: the .NET SDK publishes for macOS on
    Linux as well, and the archive built there keeps its executable bits. RCynQ-<version>-<runtime>.tar.gz lands in
    artifacts/publish.

    The programs are not signed: Linux has no codesign. The notes of the package (README.txt) say how to start it on a
    Mac: remove the quarantine and, if macOS asks for it, give the programs an ad-hoc signature there.

.PARAMETER Runtime
    The runtime identifier: osx-arm64 (default, Apple silicon) or osx-x64 (Intel).

.PARAMETER Distribution
    The WSL distribution to build in. Default: the default distribution of WSL.

.EXAMPLE
    ./Scripts/App/publish-macos.ps1
    ./Scripts/App/publish-macos.ps1 -Runtime osx-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string] $Runtime = 'osx-arm64',
    [string] $Distribution
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
& (Join-Path $scriptDir 'publish-in-wsl.ps1') -Runtime $Runtime -Distribution $Distribution
if (-not $?) {
    throw 'The build in WSL failed.'
}
