param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,

    [Parameter(Mandatory = $true)]
    [string]$TimestampServer
)

$ErrorActionPreference = "Stop"
$normalizedThumbprint = $Thumbprint.Replace(" ", "").ToUpperInvariant()
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey } |
    Select-Object -First 1

if (-not $certificate) {
    throw "The requested code-signing certificate was not found with a private key in Cert:\CurrentUser\My."
}

$signature = Set-AuthenticodeSignature `
    -LiteralPath $Path `
    -Certificate $certificate `
    -HashAlgorithm SHA256 `
    -TimestampServer $TimestampServer

if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    -not $signature.TimeStamperCertificate) {
    throw "Timestamp signing failed for '$Path': $($signature.Status) - $($signature.StatusMessage)"
}
