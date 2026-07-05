# publish.ps1 — build a release copy of Hermes into .\publish
# Run on devRyzen from the Hermes project folder:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot   # ...\Hermes
$publishDir = Join-Path $projectDir 'publish'

Write-Host "Publishing Hermes (Release) -> $publishDir"
dotnet publish "$projectDir\Hermes.csproj" -c Release -o "$publishDir"

Write-Host ""
Write-Host "Done. Run it with:  $publishDir\Hermes.exe"
