<#
.SYNOPSIS
    Publishes RCynQ (Service, CLI, UI, DbMigrator) as self-contained Release builds for Windows and creates the
    RCynQ shortcut in the artifact root.

.DESCRIPTION
    The shortcut starts RCynQ.UI, which starts RCynQ.Service in the configured role and stops it on exit.
    The shortcut stores absolute paths: run the script again after moving the artifact directory.

.PARAMETER Runtime
    The runtime identifier. Default: win-x64.

.PARAMETER Configuration
    The build configuration. Default: Release.

.PARAMETER Output
    The output root. Each application is published to <Output>/<Runtime>/<app>. Default: <repository>/artifacts/publish.

.EXAMPLE
    ./Scripts/publish-win.ps1
    ./Scripts/publish-win.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $Output
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so paths are resolved here.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
if (-not $Output) {
    $Output = Join-Path $root 'artifacts\publish'
}

$projects = [ordered]@{
    'service'    = 'Sources\Apps\RCynQ.Service\RCynQ.Service.csproj'
    'cli'        = 'Sources\Apps\RCynQ.Cli\RCynQ.Cli.csproj'
    'ui'         = 'Sources\Apps\RCynQ.UI\RCynQ.UI.csproj'
    'dbmigrator' = 'Sources\Database\RCynQ.Dbs.SQLite.DbMigrator\RCynQ.Dbs.SQLite.DbMigrator.csproj'
}

foreach ($name in $projects.Keys) {
    $project = Join-Path $root $projects[$name]
    $target = Join-Path $Output (Join-Path $Runtime $name)
    Write-Host "==> $name ($Runtime, $Configuration) -> $target"
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $target -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing '$name' failed with exit code $LASTEXITCODE."
    }
}

$runtimeRoot = (Resolve-Path (Join-Path $Output $Runtime)).Path

# Portable installation: configuration goes to config\ and all data to data\ inside this directory.
$marker = Join-Path $runtimeRoot 'rcynq.portable'
if (-not (Test-Path $marker)) {
    Set-Content -Path $marker -Encoding ascii -Value 'RCynQ portable installation: configuration is stored in config\, all data in data\. Delete this file to use the user profile instead.'
}
$uiExe = Join-Path $runtimeRoot 'ui\RCynQ.UI.exe'
if (Test-Path $uiExe) {
    $shortcutPath = Join-Path $runtimeRoot 'RCynQ.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $uiExe
        $shortcut.WorkingDirectory = Split-Path -Parent $uiExe
        $shortcut.IconLocation = "$uiExe,0"
        $shortcut.Description = 'RCynQ: starts the application and the node services'
        $shortcut.Save()
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    }

    Write-Host "Shortcut: $shortcutPath"
}

# Launcher that works from any location (the .lnk above stores absolute paths).
Set-Content -Path (Join-Path $runtimeRoot 'RCynQ.cmd') -Encoding ascii -Value @('@echo off', 'start "" "%~dp0ui\RCynQ.UI.exe" %*')

# Clean package for other nodes: without config\ and data\ (settings, keys and database of this node) and without the
# absolute-path shortcut.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$package = Join-Path (Resolve-Path $Output).Path "RCynQ-$Runtime.zip"
if (Test-Path $package) {
    [System.IO.File]::Delete($package)
}

$zip = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $runtimeRoot -Recurse -File) {
        $relative = $file.FullName.Substring($runtimeRoot.Length).TrimStart([char[]]@('\', '/'))
        $top = $relative.Split([char[]]@('\', '/'))[0]
        if ($top -eq 'config' -or $top -eq 'data' -or $relative -eq 'RCynQ.lnk') {
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
