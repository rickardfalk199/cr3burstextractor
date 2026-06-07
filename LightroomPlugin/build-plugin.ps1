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

# AppInfo.cs is the single source of truth for the product version. Read it,
# parse to major/minor/revision/build, and patch Info.lua's VERSION block so
# Lightroom Plug-in Manager shows the same version the .exe reports.
$appInfo = Join-Path $repoRoot 'Cr3BurstExtractor\AppInfo.cs'
$appInfoContent = Get-Content -Raw $appInfo
if ($appInfoContent -notmatch 'Version\s*=\s*"([^"]+)"') {
    throw "Could not find Version constant in $appInfo"
}
$version = $Matches[1]
if ($version -notmatch '^(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?$') {
    throw "AppInfo.Version '$version' is not in major.minor[.revision[.build]] format"
}
$major    = [int]$Matches[1]
$minor    = [int]$Matches[2]
$revision = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
$build    = if ($Matches[4]) { [int]$Matches[4] } else { 0 }

# Rewrite Info.lua's VERSION block in place (UTF-8, no BOM — Lightroom's Lua
# parser is happiest with that).
$infoLua = Join-Path $pluginDir 'Cr3BurstExtractor.lrplugin\Info.lua'
$infoContent = [System.IO.File]::ReadAllText($infoLua)
$newVersionBlock = "VERSION = { major = $major, minor = $minor, revision = $revision, build = $build }"
$updatedContent = [regex]::Replace($infoContent, 'VERSION\s*=\s*\{[^}]*\}', $newVersionBlock)
if ($updatedContent -eq $infoContent -and $infoContent -notmatch [regex]::Escape($newVersionBlock)) {
    throw "Could not find VERSION block to patch in $infoLua"
}
if ($updatedContent -ne $infoContent) {
    [System.IO.File]::WriteAllText($infoLua, $updatedContent, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Patched Info.lua VERSION -> { $major, $minor, $revision, $build }"
} else {
    Write-Host "Info.lua VERSION already in sync with AppInfo.cs (v$version)"
}

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
