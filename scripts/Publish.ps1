param([string]$InnoCompiler = '', [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  $env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli'
  $dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { (Resolve-Path '.tools/dotnet/dotnet.exe').Path } else { 'dotnet' }
  $version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
  $name = "StartFlow-$version-win-x64"
  $publish = Join-Path $root "artifacts/$name"
  & $dotnet publish src/StartFlow.App/StartFlow.App.csproj -c Release -r win-x64 --self-contained true -o $publish -m:1 --disable-build-servers
  if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
  Copy-Item LICENSE,THIRD-PARTY-NOTICES.md,README.md,README.en.md -Destination $publish -Force
  Copy-Item docs -Destination $publish -Recurse -Force
  $licenseDir = Join-Path $publish 'docs/licenses'
  New-Item -ItemType Directory -Force $licenseDir | Out-Null
  foreach ($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packRoot = Join-Path $root ".tools/packages/$pack"
    $packVersion = Get-ChildItem $packRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (!$packVersion) { throw "Missing runtime notices: $pack" }
    Get-ChildItem $packVersion.FullName -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY|ThirdParty)' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDir ($pack + '-' + $_.Name)) -Force }
  }
  $portable = Join-Path $root "artifacts/$name-Portable"
  if (Test-Path (Join-Path $portable 'Data')) { throw 'Portable staging contains Data. Preserve it separately and use a clean staging directory before distributing.' }
  New-Item -ItemType Directory -Force $portable | Out-Null
  Copy-Item "$publish/*" -Destination $portable -Recurse -Force
  Set-Content (Join-Path $portable 'portable.flag') 'StartFlow portable storage: ./Data' -Encoding utf8
  Compress-Archive -Path "$portable/*" -DestinationPath "artifacts/$name-Portable.zip" -Force
  if (!$SkipInstaller) {
    if (!$InnoCompiler) {
      foreach ($candidate in @('.tools/inno/ISCC.exe',"$env:LOCALAPPDATA/Programs/Inno Setup 7/ISCC.exe",'C:/Program Files (x86)/Inno Setup 6/ISCC.exe')) { if (Test-Path $candidate) { $InnoCompiler = (Resolve-Path $candidate).Path; break } }
    }
    if (!$InnoCompiler) { throw 'Inno Setup ISCC.exe not found. Pass -InnoCompiler with its full path.' }
    & $InnoCompiler "/DAppVersion=$version" "/DPublishDir=$publish" installer/StartFlow.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
  }
  Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts/SHA256SUMS.txt
} finally { Pop-Location }
