param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$RepoRoot = '',
    [string]$AbstractionsDll = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = if ([string]::IsNullOrWhiteSpace($env:UNICORNONE_REPO)) {
        'C:\Users\Bizmuth\source\repos\UnicornOne'
    } else { $env:UNICORNONE_REPO }
}
if ([string]::IsNullOrWhiteSpace($AbstractionsDll)) {
    $candidates = @(
        (Join-Path $RepoRoot "UnicornOne.Abstractions\bin\$Configuration\UnicornOne.Abstractions.dll"),
        (Join-Path $RepoRoot 'UnicornOne.Abstractions\bin\Debug\UnicornOne.Abstractions.dll'),
        (Join-Path $RepoRoot 'UnicornOne.Abstractions\bin\Release\UnicornOne.Abstractions.dll'),
        'C:\UnicornOne\UnicornOne.Abstractions.dll'
    )
    $AbstractionsDll = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($AbstractionsDll) -or -not (Test-Path -LiteralPath $AbstractionsDll -PathType Leaf)) {
    throw "UnicornOne.Abstractions.dll not found. Build the Phase 3 UnicornOne host first, then pass -AbstractionsDll 'C:\path\to\UnicornOne.Abstractions.dll' or -RepoRoot 'C:\path\to\UnicornOne'."
}
$source = (Resolve-Path -LiteralPath $AbstractionsDll).Path
$target = Join-Path $PSScriptRoot 'References\UnicornOne.Abstractions.dll'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
    (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
    Copy-Item -LiteralPath $source -Destination $target -Force
}
Write-Host "Reference: $source"

$msbuild = $null
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
    $matches = @(& $vswhere -latest -products '*' -requires 'Microsoft.Component.MSBuild' -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null)
    $msbuild = $matches | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $msbuild) {
    $cmd = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($cmd) { $msbuild = $cmd.Source }
}
if (-not $msbuild) {
    throw 'MSBuild.exe not found. Install VS2022 or its .NET Framework build tools with the MSBuild component.'
}
$solution = Join-Path $PSScriptRoot 'TwinAddons.sln'
Write-Host "Building $solution ($Configuration) with $msbuild"
& $msbuild $solution '/t:Build' "/p:Configuration=$Configuration" '/v:minimal' '/nologo'
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }
foreach ($name in @('GaNCleanliness','GaNLinearBilayerDesorption')) {
    $dll = Join-Path $PSScriptRoot "$name\bin\$Configuration\$name.dll"
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Build finished without expected DLL: $dll" }
    Write-Host "Built $dll"
}
