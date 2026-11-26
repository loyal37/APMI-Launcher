param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $projectRoot 'vendor\efmi-mcp'
$mcpOutput = Join-Path $projectRoot 'dist\mcp'
$nativeOutput = Join-Path $mcpOutput 'dist'
$null = New-Item -ItemType Directory -Path $nativeOutput -Force
$probeBuildOutput = Join-Path $projectRoot 'build\mcp-probe'
$null = New-Item -ItemType Directory -Path $probeBuildOutput -Force
& (Join-Path $sourceRoot 'probe\build.bat') $probeBuildOutput
if ($LASTEXITCODE -ne 0) { throw 'MCP probe compilation failed.' }
Copy-Item -LiteralPath (Join-Path $probeBuildOutput 'efmi_probe.dll') -Destination (Join-Path $nativeOutput 'apmi_probe.dll') -Force
foreach ($helperBuild in @('build.bat', 'build_dhash.bat')) {
    & (Join-Path $sourceRoot "server\native\$helperBuild")
    if ($LASTEXITCODE -ne 0) { throw "MCP helper compilation failed: $helperBuild" }
}
$helperOutput = Join-Path $mcpOutput 'backend\dist'
$null = New-Item -ItemType Directory -Path $helperOutput -Force
foreach ($helper in @('efmi_crc32c.dll', 'efmi_dhash.dll')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "dist\$helper") -Destination $helperOutput -Force
}

$serverOutput = Join-Path $mcpOutput 'backend\server\efmi_mcp'
$null = New-Item -ItemType Directory -Path $serverOutput -Force
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'server\efmi_mcp') -Filter '*.py' -File |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $serverOutput -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'mcp\server.py') -Destination $mcpOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'mcp\efmi_probe.ini') -Destination $nativeOutput -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'UPSTREAM.json') -Destination $mcpOutput -Force
$client = @{
    mcpServers = @{
        apmi = @{
            command = 'py'
            args = @('-3.12', (Join-Path $mcpOutput 'server.py'),
                     '--settings', (Join-Path $projectRoot 'dist\data\settings.json'))
        }
    }
}
$client | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\mcp-config.json') -Encoding utf8
Write-Output $mcpOutput
