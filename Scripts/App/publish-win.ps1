<#
.SYNOPSIS
    Publishes RCynQ (Service, CLI, UI, DbMigrator) as self-contained Release builds for Windows, creates the RCynQ.exe
    launcher in the artifact root and packs a clean copy into RCynQ-<version>-<runtime>.zip.

.DESCRIPTION
    Layout of artifacts/publish/<runtime> and of the archive (under RCynQ/):
      RCynQ.exe          the application host with the RCynQ icon, pointing at ui\RCynQ.UI.dll; it starts the UI,
                         which starts the service in the configured role and stops it on exit
      service/ cli/ ui/ dbmigrator/   the four programs
      rcynq.portable     marks a portable installation: the configuration goes to config\, all data to data\
      README.txt         first start notes in English, README.<code>.txt in the other languages (Notes/)
      LICENSE.txt        the GPL-3.0 text; THIRD-PARTY-NOTICES.txt lists the components and their licenses

    RCynQ.exe holds no absolute paths, so the folder can be moved or copied as a whole. The config and data folders of a
    node tried out from artifacts/publish/<runtime> stay where they are and never go into the archive.
    App/build-installer.ps1 packs the same folder into a setup program.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER Sign
    Signs the executables of the package with the certificate named in the environment (see Shared/sign-file.ps1), so
    the .zip package and the installer built from it carry signed programs.

.PARAMETER Output
    The output root. Each application is published to <Output>/<Runtime>/<app>. Default: <repository>/artifacts/publish.

.EXAMPLE
    ./Scripts/App/publish-win.ps1
    ./Scripts/App/publish-win.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $Output,
    [switch] $Sign
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so paths are resolved here.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $Output) {
    $Output = Join-Path $root 'artifacts\publish'
}

if ($Runtime -notmatch '^win-(x64|x86|arm64)$') {
    throw "Unsupported runtime '$Runtime'. Use win-x64, win-x86 or win-arm64."
}

$ui = Join-Path $root 'Sources\Apps\RCynQ.UI\RCynQ.UI.csproj'
$version = (& dotnet msbuild $ui -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

$projects = [ordered]@{
    'service'    = 'Sources\Apps\RCynQ.Service\RCynQ.Service.csproj'
    'cli'        = 'Sources\Apps\RCynQ.Cli\RCynQ.Cli.csproj'
    'ui'         = 'Sources\Apps\RCynQ.UI\RCynQ.UI.csproj'
    'dbmigrator' = 'Sources\Database\RCynQ.Dbs.SQLite.DbMigrator\RCynQ.Dbs.SQLite.DbMigrator.csproj'
}

$stage = Join-Path $Output $Runtime
if (Test-Path $stage) {
    # A running node of this folder holds its files open; publishing over it would fail half way.
    $stageFull = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($stageFull, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0) {
        throw "RCynQ is running from $stage (process $($running[0].Id), $($running[0].ProcessName)). Close it and publish again, or use -Output."
    }
}

# The launcher is created while the UI is published (see the RCynQRootLauncher target of RCynQ.UI.csproj).
$launcher = [System.IO.Path]::GetFullPath((Join-Path $stage 'RCynQ.exe'))
foreach ($name in $projects.Keys) {
    $project = Join-Path $root $projects[$name]
    $target = Join-Path $stage $name
    Write-Host "==> $name ($Runtime, $Configuration) -> $target"
    [string[]] $extra = @()
    if ($name -eq 'ui') {
        $extra = @("-p:RCynQRootLauncherPath=$launcher")
    }

    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $target -nologo -p:DebugType=none @extra
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing '$name' failed with exit code $LASTEXITCODE."
    }
}

$runtimeRoot = (Resolve-Path $stage).Path

# Portable installation: configuration goes to config\ and all data to data\ inside this directory.
$marker = Join-Path $runtimeRoot 'rcynq.portable'
if (-not (Test-Path $marker)) {
    Set-Content -Path $marker -Encoding ascii -Value 'RCynQ portable installation: configuration is stored in config\, all data in data\. Delete this file to use the user profile instead.'
}
if (-not (Test-Path (Join-Path $runtimeRoot 'RCynQ.exe'))) {
    throw 'The root launcher RCynQ.exe was not created.'
}

# Files of earlier versions of this script: the launcher was a .cmd file next to an absolute-path shortcut.
foreach ($old in 'RCynQ.cmd', 'RCynQ.lnk') {
    Remove-Item -LiteralPath (Join-Path $runtimeRoot $old) -Force -ErrorAction SilentlyContinue
}

# The note of the package in every language, from the templates in Notes.
. (Join-Path $scriptDir '..\Shared\package-notes.ps1')
Write-PackageNotes -Notes (Join-Path $scriptDir 'Notes') -Target $runtimeRoot -Platform win -Version $version -Runtime $Runtime
Write-PackageLicenses -Target $runtimeRoot

if ($Sign) {
    foreach ($exe in Get-ChildItem $runtimeRoot -Recurse -Filter '*.exe' -File) {
        & (Join-Path $scriptDir '..\Shared\sign-file.ps1') -File $exe.FullName
        if (-not $?) {
            throw "Signing '$($exe.FullName)' failed."
        }
    }
}

# Clean package for other nodes: without config\ and data\ (settings, keys and database of this node).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$package = Join-Path (Resolve-Path $Output).Path "RCynQ-$version-$Runtime.zip"
if (Test-Path $package) {
    [System.IO.File]::Delete($package)
}

$zip = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $runtimeRoot -Recurse -File) {
        $relative = $file.FullName.Substring($runtimeRoot.Length).TrimStart([char[]]@('\', '/'))
        $top = $relative.Split([char[]]@('\', '/'))[0]
        if ($top -eq 'config' -or $top -eq 'data') {
            continue
        }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, 'RCynQ/' + $relative.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Package for other nodes (no settings, keys or data): $package"
Write-Host "Done. Artifacts: $runtimeRoot"
