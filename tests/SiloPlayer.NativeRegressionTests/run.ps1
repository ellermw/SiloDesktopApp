param(
    [Parameter(Mandatory = $true)][string] $AppDirectory
)

$ErrorActionPreference = 'Stop'
$appPath = (Resolve-Path -LiteralPath $AppDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $appPath 'SiloPlayer.dll'))) {
    throw 'AppDirectory must contain a published SiloPlayer build.'
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$resultPath = Join-Path $repoRoot ('.codex-tmp\native-artwork-tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultPath -Force | Out-Null
$referencePath = Join-Path $resultPath 'reference'
New-Item -ItemType Directory -Path $referencePath -Force | Out-Null
Copy-Item -Path (Join-Path $appPath 'SiloPlayer.*') -Destination $referencePath
# WinUI imports the reference PRI's content paths during compilation. A publish
# can omit those loose assets, so supply the repository's fixture resources in
# the test's own staging directory without modifying the published build.
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\SiloPlayer\Assets') -Destination $referencePath -Recurse
$projectPath = Join-Path $PSScriptRoot 'SiloPlayer.NativeRegressionTests.csproj'
dotnet build $projectPath -nologo -v:q "-p:SiloPlayerAssemblyDirectory=$referencePath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$environmentNames = @('SILO_NATIVE_TEST_APP_DIRECTORY', 'SILO_NATIVE_TEST_RESULT_DIRECTORY', 'SILO_NATIVE_TEST_THEME_PATH')
$previousValues = @{}
foreach ($name in $environmentNames) { $previousValues[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:SILO_NATIVE_TEST_APP_DIRECTORY = $appPath
    $env:SILO_NATIVE_TEST_RESULT_DIRECTORY = $resultPath
    $env:SILO_NATIVE_TEST_THEME_PATH = Join-Path $repoRoot 'src\SiloPlayer\Themes\DarkTheme.xaml'
    $executable = Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows10.0.22621.0\win-x64\SiloPlayer.NativeRegressionTests.exe'
    & $executable
    $testExitCode = $LASTEXITCODE
    $resultsFile = Join-Path $resultPath 'results.txt'
    if (Test-Path -LiteralPath $resultsFile) { Get-Content -LiteralPath $resultsFile }
    else { throw 'Native regression host exited without producing results.' }
    exit $testExitCode
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousValues[$name]) }
}
