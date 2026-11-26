param([Parameter(Mandatory=$true)][string]$TestRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
$appRoot = Join-Path $TestRoot 'launcher'
$exePath = Join-Path $appRoot 'APMI Launcher.exe'
$settingsPath = Join-Path $appRoot 'data\settings.json'
$null = New-Item -ItemType Directory -Path (Split-Path $settingsPath -Parent) -Force
$settings = @{
    GameExe = (Join-Path $TestRoot 'mcp-enabled\APMIDx11Test.exe')
    OfficialLauncher = (Join-Path $TestRoot 'mcp-enabled\official.exe')
    RuntimeDirectory = (Join-Path $TestRoot 'mcp-enabled\EFMI')
    StaticBackground = $true
}
$settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8

function Wait-Control($window, [string]$id) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    do {
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($control) { return $control }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 12)
    $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { "$($_.Current.ControlType.ProgrammaticName) $($_.Current.AutomationId) $($_.Current.Name)" } |
        Select-Object -First 40 | Write-Output
    throw "UI control not found: $id"
}
function Invoke-Control($window, [string]$id) {
    $control = Wait-Control $window $id
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Open-TestWindow {
    $script:testProcess = Start-Process -FilePath $exePath -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $script:testProcess.Refresh()
        if ($script:testProcess.MainWindowHandle -ne 0) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($script:testProcess.MainWindowHandle)
            Invoke-Control $window 'settings-btn'
            return $window
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 12)
    throw 'Launcher window did not open.'
}
function Close-TestWindow {
    if ($script:testProcess -and -not $script:testProcess.HasExited) {
        [void]$script:testProcess.CloseMainWindow()
        if (-not $script:testProcess.WaitForExit(10000)) { throw 'Launcher did not close normally.' }
    }
}
function Get-Switch($window) {
    $control = Wait-Control $window 'input-enable-mcp'
    return $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
}
function Assert-Switch($window, $expected) {
    if ((Get-Switch $window).Current.ToggleState -ne $expected) { throw "Expected switch state $expected." }
}
function Assert-Saved([bool]$expected) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $saved = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
        if ($null -ne $saved.EnableMcp -and $saved.EnableMcp -eq $expected) { return }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 5)
    throw 'Saved MCP setting differs from the switch.'
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class McpWindowImage {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
$testProcess = $null
try {
    $window = Open-TestWindow
    Assert-Switch $window 'Off'
    (Get-Switch $window).Toggle()
    Invoke-Control $window 'settings-cancel-btn'
    Invoke-Control $window 'settings-btn'
    Assert-Switch $window 'Off'
    (Get-Switch $window).Toggle()

    Start-Sleep -Milliseconds 300
    $bounds = $window.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { [void][McpWindowImage]::PrintWindow($testProcess.MainWindowHandle, $dc, 2) }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save((Join-Path $TestRoot 'mcp-settings.png'))
    $bitmap.Dispose()

    Invoke-Control $window 'settings-save-btn'
    Assert-Saved $true
    Close-TestWindow
    $window = Open-TestWindow
    Assert-Switch $window 'On'
    (Get-Switch $window).Toggle()
    Invoke-Control $window 'settings-save-btn'
    Assert-Saved $false
    Close-TestWindow
    $window = Open-TestWindow
    Assert-Switch $window 'Off'
    [pscustomobject]@{ passed=$true; legacyDefaultOff=$true; cancelDiscarded=$true; enabledPersisted=$true; disabledPersisted=$true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $TestRoot 'mcp-ui-result.json') -Encoding utf8
    'PASS MCP Settings default, Cancel, Save, and both states after restart'
} finally {
    Close-TestWindow
}
