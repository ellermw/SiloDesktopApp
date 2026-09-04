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
$store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::My,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
try {
    $certificate = $store.Certificates |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1
}
finally {
    $store.Close()
}

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
