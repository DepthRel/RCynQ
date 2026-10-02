<#
.SYNOPSIS
    Runs Scripts/App/publish-in-wsl.sh for one runtime in WSL. Used by publish-linux.ps1 and publish-macos.ps1.

.PARAMETER Runtime
    The runtime identifier: linux-x64, linux-arm64, osx-x64 or osx-arm64.

.PARAMETER Distribution
    The WSL distribution to build in. Default: the default distribution of WSL.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime,
    [string] $Distribution
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if (-not (Get-Command 'wsl.exe' -ErrorAction SilentlyContinue)) {
    throw 'WSL is not found. Install it with "wsl --install" (a restart is needed) and run the script again.'
}

# A Windows path becomes /mnt/<drive>/...: the drive letter in lower case, the slashes turned around.
$windowsPath = Join-Path $scriptDir 'publish-in-wsl.sh'
$linuxPath = '/mnt/' + $windowsPath.Substring(0, 1).ToLowerInvariant() + ($windowsPath.Substring(2) -replace '\\', '/')

$arguments = @()
if ($Distribution) {
    $arguments += @('-d', $Distribution)
}
$arguments += @('--', 'bash', $linuxPath, $Runtime)

& wsl.exe @arguments
if ($LASTEXITCODE -ne 0) {
    throw "The build in WSL failed with exit code $LASTEXITCODE."
}
