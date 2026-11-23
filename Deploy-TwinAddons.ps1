param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$TargetDir = 'C:\UnicornOne\Plugins\Twin',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$names = @('GaNCleanliness','GaNLinearBilayerDesorption')
$files = @()
foreach ($name in $names) {
    $file = Join-Path $PSScriptRoot "$name\bin\$Configuration\$name.dll"
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Build TwinAddons first. Missing $file"
    }
    $files += $file
}
Write-Host "Deploying to: $TargetDir"
foreach ($file in $files) { Write-Host "  $([IO.Path]::GetFileName($file))" }
if ($CheckOnly) { Write-Host 'CheckOnly: no files copied.'; exit 0 }
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($file in $files) {
    $dest = Join-Path $TargetDir ([IO.Path]::GetFileName($file))
    if (Test-Path -LiteralPath $dest -PathType Leaf) {
        Copy-Item -LiteralPath $dest -Destination ($dest + '.' + $stamp + '.bak') -Force
    }
    Copy-Item -LiteralPath $file -Destination $dest -Force
    $expected = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw "Deployment verification failed: $dest" }
    Write-Host "Deployed $dest"
}
Write-Host 'Restart UnicornOne to discover these DLLs, then enable them in Twin Cockpit > Addons.'
Write-Host 'Do NOT deploy References\UnicornOne.Abstractions.dll: the host provides that assembly.'
