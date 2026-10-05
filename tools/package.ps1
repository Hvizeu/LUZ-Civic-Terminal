param([ValidateSet('win-x64','linux-x64','osx-x64','osx-arm64','all')][string]$Rid = 'all')
$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot 'package.py') --rid $Rid
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
