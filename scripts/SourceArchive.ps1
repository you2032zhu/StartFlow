$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  $version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
  $archive = Join-Path $root "artifacts/StartFlow-$version-Source.zip"
  New-Item -ItemType Directory -Force artifacts | Out-Null
  $gitCommand = Get-Command git -ErrorAction SilentlyContinue
  if ($gitCommand -and (Test-Path (Join-Path $root '.git'))) {
    & git rev-parse --verify HEAD 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
      & git diff --quiet HEAD --
      if ($LASTEXITCODE -ne 0) { throw 'Commit source changes before creating a corresponding source archive.' }
      & git archive --format=zip "--output=$archive" HEAD
      if ($LASTEXITCODE -ne 0) { throw 'Git source archive failed' }
      Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts/SHA256SUMS.txt
      return
    }
  }
  $stage = Join-Path $root '.tools/source-stage'
  if (Test-Path $stage) {
    $resolved = (Resolve-Path $stage).Path
    if (!$resolved.StartsWith((Join-Path $root '.tools') + [IO.Path]::DirectorySeparatorChar)) { throw 'Unsafe stage path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
  New-Item -ItemType Directory -Force $stage,artifacts | Out-Null
  foreach ($file in @('StartFlow.sln','Directory.Build.props','global.json','.gitignore','.gitattributes','README.md','README.en.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md')) { Copy-Item -LiteralPath $file -Destination $stage }
  foreach ($folder in @('src','tests','docs','scripts','installer','.github','examples')) {
    Get-ChildItem -LiteralPath $folder -File -Recurse -Force | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
      $relative = $_.FullName.Substring($root.Length + 1)
      $target = Join-Path $stage $relative
      New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
      Copy-Item -LiteralPath $_.FullName -Destination $target
    }
  }
  $archive = Join-Path $root "artifacts/StartFlow-$version-Source.zip"
  if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive)
  Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts/SHA256SUMS.txt
} finally { Pop-Location }
