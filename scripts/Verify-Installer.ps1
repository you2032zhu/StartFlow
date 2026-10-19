param([string]$Installer = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Installer) { $Installer = Join-Path $root 'artifacts/StartFlow-1.0.0-win-x64-Setup.exe' }
$Installer = (Resolve-Path $Installer).Path
$data = Join-Path $env:LOCALAPPDATA 'StartFlow'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5819456-F3A5-450B-A8F1-8E964830FBA3}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path $env:APPDATA 'Microsoft/Windows/Start Menu/Programs/StartFlow.lnk'
function Read-Startup {
  $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
  try { if ($key) { return $key.GetValue('StartFlow') } } finally { if ($key) { $key.Dispose() } }
}
if ((Test-Path $data) -or (Test-Path $uninstallKey) -or (Test-Path $shortcut) -or (Read-Startup)) { throw 'Existing StartFlow user data/install/startup detected. Use a clean Windows test account; this script will not modify existing state.' }
$install = Join-Path $root '.test-data/installed-app'
New-Item -ItemType Directory -Force $install,$data | Out-Null
$reference = Join-Path $root '.test-data/referenced-document.txt'
Set-Content $reference 'Must survive uninstall.'
$configuration = Get-Content (Join-Path $root 'examples/example-config.json') -Raw | ConvertFrom-Json
$configuration.Plans[0].Name = 'Installer upgrade preservation marker'
$configuration.Plans[0].Items[1].Target = $reference
$configuration | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $data 'config.json') -Encoding utf8
$export = Join-Path $data 'keep-export.json'; Set-Content $export '{"testExport":true}'
$hash = (Get-FileHash (Join-Path $data 'config.json')).Hash
function Install-TestCopy {
  $process = Start-Process $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $root 'artifacts/installer-install.log')+'"')) -WindowStyle Hidden -PassThru
  if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Installation failed' }
}
try {
  Install-TestCopy
  if (!(Test-Path (Join-Path $install 'StartFlow.exe')) -or !(Test-Path $uninstallKey) -or !(Test-Path $shortcut)) { throw 'Installer registration or shortcut missing' }
  Write-Output 'PASS real current-user installation, application files, registration and shortcut'
  $command = '"' + (Join-Path $install 'StartFlow.exe') + '" --login'
  Set-ItemProperty -Path $runKey -Name StartFlow -Value $command
  Install-TestCopy
  if ((Get-FileHash (Join-Path $data 'config.json')).Hash -ne $hash) { throw 'Upgrade changed configuration' }
  if ((Get-ItemPropertyValue $runKey -Name StartFlow) -ne $command) { throw 'Upgrade damaged startup entry' }
  Write-Output 'PASS real in-place upgrade preserves configuration and owned startup entry'
  $uninstaller = Join-Path $install 'unins000.exe'
  $process = Start-Process $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $root 'artifacts/installer-uninstall.log')+'"')) -WindowStyle Hidden -PassThru
  if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Uninstall failed' }
  # Inno may hand off to its temporary uninstaller process before the original executable exits.
  $deadline = [DateTime]::UtcNow.AddSeconds(10)
  do {
    $remaining = @{
      Registration = (Test-Path $uninstallKey)
      Shortcut = (Test-Path $shortcut)
      Configuration = (Test-Path (Join-Path $data 'config.json'))
      Startup = [bool](Read-Startup)
    }
    $any = $remaining.Registration -or $remaining.Shortcut -or $remaining.Configuration -or $remaining.Startup
    if ($any) { Start-Sleep -Milliseconds 200 }
  } while ($any -and [DateTime]::UtcNow -lt $deadline)
  if ($any) { throw ('Uninstall left owned state behind: ' + ($remaining | ConvertTo-Json -Compress)) }
  if (!(Test-Path $reference) -or !(Test-Path $export)) { throw 'Uninstall removed referenced or exported content' }
  Write-Output 'PASS real uninstall clears owned config, startup, shortcuts and registration; preserves reference and export'
} finally {
  if (Test-Path $export) { Remove-Item -LiteralPath $export -Force }
  if ((Test-Path $data) -and !(Get-ChildItem -LiteralPath $data -Force)) { Remove-Item -LiteralPath $data }
}
