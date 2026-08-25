param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $workspace 'work\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $workspace 'work\dotnet-cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $workspace 'work\nuget-packages'

& $dotnet restore (Join-Path $workspace 'FramePathLab.slnx') --configfile (Join-Path $workspace 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $workspace 'FramePathLab.slnx') --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet run --project (Join-Path $workspace 'tests\FramePathLab.Tests\FramePathLab.Tests.csproj') --configuration $Configuration --no-build
exit $LASTEXITCODE
