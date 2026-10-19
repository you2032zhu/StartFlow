param([switch]$WindowsIntegration)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  $env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli'
  $dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { (Resolve-Path '.tools/dotnet/dotnet.exe').Path } else { 'dotnet' }
  & $dotnet build StartFlow.sln -c Release -m:1 --disable-build-servers
  if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
  $testArgs = @('tests/StartFlow.Tests/bin/Release/net10.0-windows/StartFlow.Tests.dll')
  if ($WindowsIntegration) { $testArgs += '--windows-integration' }
  & $dotnet @testArgs
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
