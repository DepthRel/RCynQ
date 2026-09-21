<#
.SYNOPSIS
    Signs one file with an Authenticode code signing certificate (SHA-256, with a timestamp).

.DESCRIPTION
    Used by publish-win.ps1 (the executables of the package) and by Inno Setup (the installer and the uninstaller), so
    the whole delivery is signed with one certificate. It needs no SDK: the signature is made with
    Set-AuthenticodeSignature. The certificate comes from the environment, so no secret appears in a command line or
    in the repository:

      RCYNQ_SIGN_THUMBPRINT     the thumbprint of a certificate in the certificate store of the current user, or
      RCYNQ_SIGN_PFX            the path of a .pfx file, with
      RCYNQ_SIGN_PFX_PASSWORD   its password;
      RCYNQ_SIGN_TIMESTAMP_URL  the timestamp server; default http://timestamp.digicert.com.

    The timestamp keeps the signature valid after the certificate expires. Without a certificate the script fails, so a
    build that was asked to sign never silently produces an unsigned file.

.PARAMETER File
    The file to sign.

.EXAMPLE
    $env:RCYNQ_SIGN_THUMBPRINT = '0123456789ABCDEF0123456789ABCDEF01234567'
    ./Scripts/sign-file.ps1 artifacts\publish\RCynQ-1.0.0-setup.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $File
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
    throw "The file to sign does not exist: $File"
}

$certificate = $null
if ($env:RCYNQ_SIGN_PFX) {
    if (-not (Test-Path -LiteralPath $env:RCYNQ_SIGN_PFX -PathType Leaf)) {
        throw "The certificate file does not exist: $env:RCYNQ_SIGN_PFX"
    }

    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 `
        -ArgumentList $env:RCYNQ_SIGN_PFX, $env:RCYNQ_SIGN_PFX_PASSWORD
}
elseif ($env:RCYNQ_SIGN_THUMBPRINT) {
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $env:RCYNQ_SIGN_THUMBPRINT -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $certificate) {
        throw "No certificate with a private key and the thumbprint $env:RCYNQ_SIGN_THUMBPRINT was found."
    }
}
else {
    throw 'No signing certificate: set RCYNQ_SIGN_THUMBPRINT, or RCYNQ_SIGN_PFX and RCYNQ_SIGN_PFX_PASSWORD.'
}

$timestamp = if ($env:RCYNQ_SIGN_TIMESTAMP_URL) { $env:RCYNQ_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
$signature = Set-AuthenticodeSignature -LiteralPath $File -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $timestamp
if (-not $signature.SignerCertificate) {
    throw "Signing '$File' failed: $($signature.StatusMessage)"
}

Write-Host "Signed: $File ($($certificate.Subject))"
