param(
    [Alias('EfmiSource')][string]$PackageSource,
    [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'dist'
& (Join-Path $PSScriptRoot 'build-mcp.ps1')
$publishArgs = @('publish', (Join-Path $projectRoot 'src\APMILauncher.csproj'), '-c', 'Release', '-o', $output, '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-p:PublishSingleFile=false')
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }

$runtime = Join-Path $output 'APMI'
if (-not (Test-Path -LiteralPath $runtime)) {
    if ([string]::IsNullOrWhiteSpace($PackageSource)) { throw 'Supply -PackageSource with an existing APMI or EFMI package folder for the first build.' }
    $source = (Resolve-Path -LiteralPath $PackageSource).Path
    foreach ($relative in @('d3d11.dll', 'd3dcompiler_47.dll', 'd3dx.ini')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $relative))) { throw "Incomplete package source: $relative" }
    }
    if (-not ((Test-Path -LiteralPath (Join-Path $source 'Core\APMI\main.ini')) -or
              (Test-Path -LiteralPath (Join-Path $source 'Core\EFMI\main.ini')))) { throw 'Package source requires Core\APMI\main.ini or Core\EFMI\main.ini.' }
    $staging = Join-Path $output ('APMI.staging-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $staging
    foreach ($file in @('d3d11.dll','d3dcompiler_47.dll','d3dx.ini')) {
        Copy-Item -LiteralPath (Join-Path $source $file) -Destination $staging
    }
    if (Test-Path -LiteralPath (Join-Path $source 'README.md')) { Copy-Item -LiteralPath (Join-Path $source 'README.md') -Destination $staging }
    if (Test-Path -LiteralPath (Join-Path $source 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination $staging }
    if (Test-Path -LiteralPath (Join-Path $source 'UPSTREAM.json')) { Copy-Item -LiteralPath (Join-Path $source 'UPSTREAM.json') -Destination $staging }
    Copy-Item -LiteralPath (Join-Path $source 'Core') -Destination $staging -Recurse
    foreach ($folder in @('Mods','ShaderCache','ShaderFixes')) { $null = New-Item -ItemType Directory -Path (Join-Path $staging $folder) }
    foreach ($dll in @('d3d11.dll','d3dcompiler_47.dll')) {
        if ((Get-FileHash -LiteralPath (Join-Path $source $dll)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $staging $dll)).Hash) { throw "Runtime copy verification failed: $dll" }
    }
    $resolvedOutput = [IO.Path]::GetFullPath($output).TrimEnd('\') + '\'
    foreach ($candidate in @($staging, $runtime)) {
        if (-not [IO.Path]::GetFullPath($candidate).StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging must stay inside dist.' }
    }
    Move-Item -LiteralPath $staging -Destination $runtime
}
foreach($file in @('README.md','THIRD_PARTY_NOTICES.md','LICENSE')) {
    $sourceFile = Join-Path $projectRoot $file
    if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination $output -Force }
}
$wallpapersSource = Join-Path $projectRoot 'wallpapers'
$wallpapersDest = Join-Path $output 'wallpapers'
if (Test-Path -LiteralPath $wallpapersSource) {
    $null = New-Item -ItemType Directory -Path $wallpapersDest -Force
    foreach ($asset in @('official-launcher-background.mp4', 'official-launcher-background.png')) {
        $srcAsset = Join-Path $wallpapersSource $asset
        if (Test-Path -LiteralPath $srcAsset) {
            Copy-Item -LiteralPath $srcAsset -Destination (Join-Path $wallpapersDest $asset) -Force
        }
    }
}
Write-Output (Join-Path $output 'APMI Launcher.exe')
