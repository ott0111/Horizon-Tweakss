$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $root '.dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) {
  throw 'The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and try again.'
}

& $dotnet restore Horizon.sln
if ($LASTEXITCODE -ne 0) { throw "Solution restore failed with exit code $LASTEXITCODE." }

& $dotnet test tests/Horizon.Tests/Horizon.Tests.csproj --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Release tests failed with exit code $LASTEXITCODE." }

& $dotnet restore src/Horizon.App/Horizon.App.csproj --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw "Windows runtime restore failed with exit code $LASTEXITCODE." }

& $dotnet publish src/Horizon.App/Horizon.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts/win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -p:DebugSymbols=false `
  -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw "Windows release publish failed with exit code $LASTEXITCODE." }

Write-Host "Horizon release created at artifacts/win-x64/Horizon.exe"
