param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
python -m pip install -r scripts/requirements.txt
if ($LASTEXITCODE) { throw 'Artwork dependencies failed' }
python scripts/prepare-artwork.py
if ($LASTEXITCODE) { throw 'Artwork preparation failed' }
dotnet run --project tests/Amplifier.Core.Tests -c Release
if ($LASTEXITCODE) { throw 'Core tests failed' }
dotnet publish src/Amplifier.Windows/Amplifier.Windows.csproj -c Release -r $Runtime --self-contained true -o ".artifacts/preview-$Runtime"
if ($LASTEXITCODE) { throw 'Windows publish failed' }
Write-Host "Unsigned development build: .artifacts/preview-$Runtime/Amplifier.exe --preview"
