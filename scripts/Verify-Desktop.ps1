param([string]$Executable = '', [switch]$SaveDocumentationScreenshots)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Executable) { $Executable = Join-Path $root 'artifacts/StartFlow-1.0.0-win-x64/StartFlow.exe' }
$Executable = (Resolve-Path $Executable).Path
$qa = Join-Path $root 'artifacts/qa'
New-Item -ItemType Directory -Force $qa | Out-Null
foreach ($variant in @(@('zh-CN','Light'),@('en','Dark'),@('zh-CN','System'))) {
  $name = $variant[0] + '-' + $variant[1]
  $data = Join-Path $root ('.test-data/render-' + $name)
  New-Item -ItemType Directory -Force $data | Out-Null
  $config = Get-Content (Join-Path $root 'examples/example-config.json') -Raw | ConvertFrom-Json
  $config.Settings.Language = $variant[0]; $config.Settings.Theme = $variant[1]
  $config.Settings.CloseBehavior = 'Exit'
  $config | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $data 'config.json') -Encoding utf8
  $png = Join-Path $data 'actual-window.png'
  if (Test-Path $png) { Remove-Item -LiteralPath $png -Force }
  $process = Start-Process $Executable -ArgumentList @('--test-data',('"'+$data+'"'),'--test-render') -WindowStyle Hidden -PassThru
  try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path $png) -and [DateTime]::UtcNow -lt $deadline -and !$process.HasExited) { Start-Sleep -Milliseconds 200 }
    if (!(Test-Path $png)) { throw "Actual WPF render failed: $name" }
    $secondary = Start-Process $Executable -ArgumentList @('--test-data',('"'+$data+'"')) -WindowStyle Hidden -PassThru
    if (!$secondary.WaitForExit(5000) -or $process.HasExited) { throw 'Single-instance activation failed' }
    Copy-Item -LiteralPath $png -Destination (Join-Path $qa ($name + '.png')) -Force
    if ($SaveDocumentationScreenshots) {
      New-Item -ItemType Directory -Force (Join-Path $root 'docs/screenshots') | Out-Null
      Copy-Item -LiteralPath $png -Destination (Join-Path $root ('docs/screenshots/' + $name + '.png')) -Force
    }
    Write-Output "PASS actual WPF $name and second-process activation"
  } finally {
    $close = Start-Process $Executable -ArgumentList @('--test-data',('"'+$data+'"'),'--test-close') -WindowStyle Hidden -PassThru
    $close.WaitForExit(5000) | Out-Null
    if (!$process.WaitForExit(5000)) { throw 'Test instance did not exit gracefully' }
    Write-Output "PASS actual main-window Close to Exit lifecycle: $name"
  }
}
$loginData = Join-Path $root '.test-data/login-entry'
New-Item -ItemType Directory -Force $loginData | Out-Null
$marker = Join-Path $loginData 'must-not-auto-run.txt'
if (Test-Path $marker) { Remove-Item -LiteralPath $marker -Force }
$command = "[IO.File]::WriteAllText('" + $marker.Replace("'","''") + "','unexpected execution')"
$configuration = @{
  FormatVersion=1; Settings=@{Language='zh-CN';Theme='System';CloseBehavior='Tray';ProjectUrl=''}
  Plans=@(@{Id=[guid]::NewGuid().ToString();Name='Login confirmation check';Mode='Timeline';InitialDelaySeconds=0;Items=@(@{Id=[guid]::NewGuid().ToString();Name='Must wait for confirmation';Kind='Command';Shell='PowerShell';Target=$command;Enabled=$true;Seconds=0})})
}
$configuration | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $loginData 'config.json') -Encoding utf8
$process = Start-Process $Executable -ArgumentList @('--test-data',('"'+$loginData+'"'),'--login') -WindowStyle Hidden -PassThru
try {
  Start-Sleep -Milliseconds 1500
  if ($process.HasExited -or (Test-Path $marker)) { throw 'Login confirmation gate failed' }
  $saved = Get-Content (Join-Path $loginData 'config.json') -Raw | ConvertFrom-Json
  if (!$saved.Settings.LastHandledSession) { throw 'Login session marker was not saved' }
  Write-Output 'PASS actual login entry opens confirmation and does not run overdue item without consent'
} finally {
  $close = Start-Process $Executable -ArgumentList @('--test-data',('"'+$loginData+'"'),'--test-exit') -WindowStyle Hidden -PassThru
  $close.WaitForExit(5000) | Out-Null
  if (!$process.WaitForExit(5000)) { throw 'Login test instance did not exit' }
}
