param([Parameter(Mandatory = $true)][string] $AppDirectory)
$ErrorActionPreference = 'Stop'
$appPath = (Resolve-Path -LiteralPath $AppDirectory).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$referencePath = Join-Path $repoRoot ('.codex-tmp\playback-integration-reference-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $referencePath -Force | Out-Null
Copy-Item -Path (Join-Path $appPath '*.dll') -Destination $referencePath
Copy-Item -Path (Join-Path $appPath '*.pri') -Destination $referencePath
# WinUI references PRI content at build time, including assets embedded in the
# published application. Supply the source assets only in the test reference.
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\SiloPlayer\Assets') -Destination $referencePath -Recurse
dotnet build (Join-Path $PSScriptRoot 'SiloPlayer.PlaybackIntegrationTests.csproj') -nologo -v:q "-p:SiloPlayerAssemblyDirectory=$referencePath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows10.0.22621.0\win-x64\SiloPlayer.PlaybackIntegrationTests.exe')
exit $LASTEXITCODE
