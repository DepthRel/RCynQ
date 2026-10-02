<#
.SYNOPSIS
    Builds every package of an RCynQ release on this machine, one after another, and lists them with checksums.

.DESCRIPTION
    First the programs for every system, then the installer, in this order:
      1. Windows        App/publish-win.ps1                    RCynQ-<version>-win-x64.zip (and artifacts/publish/win-x64)
      2. Linux          App/publish-linux.ps1                  RCynQ-<version>-linux-x64.tar.gz and the .deb installer (in WSL)
      3. MacOS          App/publish-macos.ps1                  RCynQ-<version>-osx-arm64.tar.gz, Apple silicon (in WSL)
      4. MacOSIntel     App/publish-macos.ps1 -Runtime osx-x64 RCynQ-<version>-osx-x64.tar.gz, Intel (in WSL)
      5. WindowsSetup   App/build-installer.ps1 -SkipPublish   RCynQ-<version>-setup.exe from the folder of step 1

    Every step runs in a PowerShell process of its own, so one script cannot leave a changed folder or variable to the
    next. The first failed step stops the bundle (see -ContinueOnError). At the end the packages of the version found
    in artifacts/publish are listed with their size and SHA-256, and the checksums are written to
    artifacts/publish/SHA256SUMS-<version>.txt, ready to be attached to the release.

    What the machine needs: the .NET SDK, WSL with a Debian or Ubuntu distribution (the Linux and macOS packages; the
    first run installs the .NET SDK there) and Inno Setup 6+ (the installer). Each step checks its own tools and says
    what is missing. The macOS packages built this way are not signed (see their README.txt).

.PARAMETER Skip
    The steps to leave out, by name: Windows, Linux, MacOS, MacOSIntel, WindowsSetup. Leaving out Windows makes
    WindowsSetup pack the folder an earlier run left in artifacts/publish/win-x64.

.PARAMETER ContinueOnError
    Runs the remaining steps after a failed one; the bundle still ends with an error when any step failed.

.PARAMETER Sign
    Signs the Windows programs, the installer and the uninstaller (see Shared/sign-file.ps1).

.EXAMPLE
    ./Scripts/build-release.ps1
    ./Scripts/build-release.ps1 -Skip MacOS,MacOSIntel
    ./Scripts/build-release.ps1 -ContinueOnError
#>
[CmdletBinding()]
param(
    [string[]] $Skip = @(),
    [switch] $ContinueOnError,
    [switch] $Sign
)

$ErrorActionPreference = 'Stop'

$stepNames = 'Windows', 'Linux', 'MacOS', 'MacOSIntel', 'WindowsSetup'

# "powershell -File" passes "A,B" as one string, a PowerShell prompt as a list: both are read the same way.
$Skip = @($Skip | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $Skip) {
    if ($stepNames -notcontains $name) {
        throw "Unknown step '$name'. The steps are: $($stepNames -join ', ')."
    }
}

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..')).Path
$output = Join-Path $root 'artifacts\publish'

# Every package carries the version of Directory.Build.props; the UI project reads it like the build scripts do.
$ui = Join-Path $root 'Sources\Apps\RCynQ.UI\RCynQ.UI.csproj'
$version = (& dotnet msbuild $ui -nologo -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw 'Cannot read the version from the project.'
}

[string[]] $signArgument = @()
if ($Sign) {
    $signArgument = @('-Sign')
}

$steps = @(
    @{ Name = 'Windows'; Script = 'App\publish-win.ps1'; Arguments = $signArgument },
    @{ Name = 'Linux'; Script = 'App\publish-linux.ps1'; Arguments = @() },
    @{ Name = 'MacOS'; Script = 'App\publish-macos.ps1'; Arguments = @('-Runtime', 'osx-arm64') },
    @{ Name = 'MacOSIntel'; Script = 'App\publish-macos.ps1'; Arguments = @('-Runtime', 'osx-x64') },
    @{ Name = 'WindowsSetup'; Script = 'App\build-installer.ps1'; Arguments = @('-SkipPublish') + $signArgument }
)

# The steps run in the same PowerShell the bundle runs in (Windows PowerShell or PowerShell 7).
$shell = (Get-Process -Id $PID).Path
$results = @()
$stopped = $false

Write-Host "==> RCynQ $version release bundle"
foreach ($step in $steps) {
    if ($stopped) {
        $results += [pscustomobject]@{ Step = $step.Name; Result = 'not run'; Time = '' }
        continue
    }

    if ($Skip -contains $step.Name) {
        $results += [pscustomobject]@{ Step = $step.Name; Result = 'skipped'; Time = '' }
        continue
    }

    Write-Host ''
    Write-Host "==> $($step.Name): $($step.Script) $($step.Arguments -join ' ')"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    [string[]] $stepArguments = $step.Arguments
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir $step.Script) @stepArguments
    $code = $LASTEXITCODE
    $watch.Stop()

    $time = '{0:mm\:ss}' -f $watch.Elapsed
    $result = if ($code -eq 0) { 'done' } else { "failed (exit code $code)" }
    $results += [pscustomobject]@{ Step = $step.Name; Result = $result; Time = $time }

    if ($code -ne 0 -and -not $ContinueOnError) {
        $stopped = $true
    }
}

Write-Host ''
Write-Host '==> Steps'
$results | Format-Table -AutoSize | Out-String | Write-Host

# The packages of this version: the archives and the installers in the root of artifacts/publish.
$packages = @(Get-ChildItem $output -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "RCynQ-$version-*" -and
        ($_.Name.EndsWith('.zip') -or $_.Name.EndsWith('.tar.gz') -or $_.Name.EndsWith('.exe') -or $_.Name.EndsWith('.deb'))
    } | Sort-Object Name)

if ($packages.Count -gt 0) {
    $sums = foreach ($package in $packages) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash.ToLowerInvariant()
        [pscustomobject]@{ Package = $package.Name; Size = '{0:N1} MB' -f ($package.Length / 1MB); Sha256 = $hash }
    }

    Write-Host "==> Packages in $output"
    $sums | Format-Table -AutoSize | Out-String -Width 400 | Write-Host

    # The usual sha256sum format, so "sha256sum -c" checks a download.
    $sumsFile = Join-Path $output "SHA256SUMS-$version.txt"
    $lines = foreach ($sum in $sums) {
        "$($sum.Sha256)  $($sum.Package)"
    }
    [System.IO.File]::WriteAllText($sumsFile, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Checksums: $sumsFile"
}
else {
    Write-Warning "No packages of version $version were found in $output."
}

$failed = @($results | Where-Object { $_.Result -like 'failed*' })
if ($failed.Count -gt 0) {
    throw "The release bundle is incomplete: $(($failed | ForEach-Object { $_.Step }) -join ', ') failed."
}

Write-Host "The packages of RCynQ $version are ready in $output."
