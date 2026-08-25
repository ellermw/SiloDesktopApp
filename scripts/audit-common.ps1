# Shared safety policy for scripts that inspect the authoritative Silo server.
$OfficialSiloOriginPatterns = @(
    '^https://github\.com/Silo-Server/silo-server(?:\.git)?$',
    '^git@github\.com:Silo-Server/silo-server(?:\.git)?$',
    '^ssh://git@github\.com/Silo-Server/silo-server(?:\.git)?$'
)

function Assert-OfficialSiloOrigin {
    param([Parameter(Mandatory = $true)][string]$OriginUrl)

    foreach ($pattern in $OfficialSiloOriginPatterns) {
        if ($OriginUrl -match $pattern) { return }
    }

    throw "Refusing to audit unexpected origin '$OriginUrl'; expected the official Silo GitHub repository."
}
