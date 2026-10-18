# Builds YuzuPresence_Playnite.pext (needs the .NET SDK 6 or newer: https://dotnet.microsoft.com/download)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet build YuzuPresence.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out  = Join-Path $PSScriptRoot "bin\Release\net462"
$pack = Join-Path $PSScriptRoot "pack"
if (Test-Path $pack) { Remove-Item $pack -Recurse -Force }
New-Item -ItemType Directory -Path $pack | Out-Null
Copy-Item (Join-Path $out "YuzuPresence.dll") $pack
Copy-Item (Join-Path $PSScriptRoot "extension.yaml") $pack
Copy-Item (Join-Path $PSScriptRoot "icon.png") $pack

$zip  = Join-Path $PSScriptRoot "YuzuPresence_Playnite.zip"
$pext = Join-Path $PSScriptRoot "YuzuPresence_Playnite.pext"
if (Test-Path $zip)  { Remove-Item $zip }
if (Test-Path $pext) { Remove-Item $pext }
Compress-Archive -Path (Join-Path $pack "*") -DestinationPath $zip
Rename-Item $zip $pext
Write-Host "Done: $pext"
