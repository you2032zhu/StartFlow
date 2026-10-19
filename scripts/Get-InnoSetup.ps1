$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $root '.tools/innosetup-7.1.0-x64.exe'
New-Item -ItemType Directory -Force (Split-Path $installer -Parent) | Out-Null
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $installer
$expected = '0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
if ((Get-FileHash $installer -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'Inno Setup SHA-256 mismatch' }
if ((Get-AuthenticodeSignature $installer).Status -ne 'Valid') { throw 'Inno Setup Authenticode validation failed' }
$destination = Join-Path $root '.tools/inno'
$process = Start-Process $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path (Join-Path $destination 'ISCC.exe'))) { throw 'Inno Setup installation failed' }
