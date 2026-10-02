<#
.SYNOPSIS
    Builds the Linux packages of RCynQ from Windows, inside WSL.

.DESCRIPTION
    Runs Scripts/App/publish-in-wsl.sh in a WSL distribution: the sources are copied into the Linux file system (so the
    archive keeps its executable bits), built there, and the finished RCynQ-<version>-<runtime>.tar.gz and, where the
    distribution has dpkg-deb (Debian, Ubuntu), RCynQ-<version>-<runtime>.deb land in artifacts/publish next to the
    Windows packages.

    The first run installs the .NET SDK into ~/.dotnet of the distribution (official install script, no sudo) and
    takes a few minutes; later runs are faster.

.PARAMETER Runtime
    The runtime identifier: linux-x64 (default) or linux-arm64.

.PARAMETER Distribution
    The WSL distribution to build in. Default: the default distribution of WSL.

.EXAMPLE
    ./Scripts/App/publish-linux.ps1
    ./Scripts/App/publish-linux.ps1 -Runtime linux-arm64 -Distribution Ubuntu
#>
[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string] $Runtime = 'linux-x64',
    [string] $Distribution
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
& (Join-Path $scriptDir 'publish-in-wsl.ps1') -Runtime $Runtime -Distribution $Distribution
if (-not $?) {
    throw 'The build in WSL failed.'
}
