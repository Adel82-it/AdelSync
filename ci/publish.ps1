$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish "src/AdelSync/AdelSync.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=false `
  -o $out

if (-not (Test-Path (Join-Path $out "AdelSync.exe"))) {
  throw "AdelSync.exe not found in dist/"
}

Write-Host "OK:" (Get-Item (Join-Path $out "AdelSync.exe")).FullName
Write-Host "Size:" (Get-Item (Join-Path $out "AdelSync.exe")).Length
