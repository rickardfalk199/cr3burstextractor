#requires -Version 5.1
# Publishes the CLI as a self-contained single-file exe and drops it into the
# plugin's bin/ folder so the .lrplugin is ready to be loaded by Lightroom.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$pluginDir   = $PSScriptRoot
$repoRoot    = Split-Path -Parent $pluginDir
$cliProject  = Join-Path $repoRoot 'Cr3BurstExtractor.Cli\Cr3BurstExtractor.Cli.csproj'
$publishDir  = Join-Path $pluginDir 'publish'
$pluginBin   = Join-Path $pluginDir 'Cr3BurstExtractor.lrplugin\bin'

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found at $cliProject"
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
if (Test-Path $pluginBin) {
    Remove-Item -Recurse -Force $pluginBin
}
New-Item -ItemType Directory -Path $pluginBin | Out-Null

Write-Host "Publishing $cliProject ($Configuration, $Runtime)..."
& dotnet publish $cliProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host "Copying publish output to $pluginBin"
Copy-Item -Path (Join-Path $publishDir '*') -Destination $pluginBin -Recurse

# Drop debug symbols — Lightroom doesn't need them and they bloat the bundle.
Get-ChildItem -Path $pluginBin -Filter '*.pdb' | Remove-Item -Force

$exe = Join-Path $pluginBin 'Cr3BurstExtractor.Cli.exe'
if (-not (Test-Path $exe)) {
    throw "Expected exe not found after publish: $exe"
}

# Read version from Info.lua so the zip filename always matches what Lightroom
# shows in Plug-in Manager.
$infoLua = Join-Path $pluginDir 'Cr3BurstExtractor.lrplugin\Info.lua'
$infoContent = Get-Content -Raw $infoLua
if ($infoContent -notmatch 'VERSION\s*=\s*\{\s*major\s*=\s*(\d+)\s*,\s*minor\s*=\s*(\d+)\s*,\s*revision\s*=\s*(\d+)') {
    throw "Could not parse VERSION block from $infoLua"
}
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$pluginFolder = Join-Path $pluginDir 'Cr3BurstExtractor.lrplugin'
$zipPath = Join-Path $pluginDir "Cr3BurstExtractor.lrplugin-v$version.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Write-Host "Zipping plugin -> $zipPath"
Compress-Archive -Path $pluginFolder -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Plugin built successfully (v$version)."
Write-Host "  Plugin folder: $pluginFolder"
Write-Host "  Release zip:   $zipPath"
Write-Host ""
Write-Host "In Lightroom Classic:"
Write-Host "  File -> Plug-in Manager -> Add -> select the Cr3BurstExtractor.lrplugin folder"
