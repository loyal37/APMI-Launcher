# APMI Launcher architecture

## Source map

| File | Responsibility |
| --- | --- |
| [APMILauncher.csproj](../src/APMILauncher.csproj) | Targets `net9.0-windows` and `win-x64`, enables WPF and embeds the web assets |
| [App.xaml.cs](../src/App.xaml.cs) | Opens the main window or dispatches `--worker <session-directory>` |
| [MainWindow.xaml.cs](../src/MainWindow.xaml.cs) | Hosts WebView2, handles native pickers and settings, starts the worker and displays its status |
| [ui/index.html](../src/ui/index.html), [ui/styles.css](../src/ui/styles.css), [ui/app.js](../src/ui/app.js) | Render launcher controls, settings, wallpaper and progress through a JSON message bridge |
| [Settings.cs](../src/Settings.cs) | Defines and validates settings, prepares the selected package and writes settings and state atomically |
| [LauncherSession.cs](../src/LauncherSession.cs) | Owns the worker lifecycle, cancellation, session state and initialization checks |
| [Native.cs](../src/Native.cs) | Handles Windows executable validation, process identity and runtime module loading and verification |
| [McpProbe.cs](../src/McpProbe.cs) | Verifies the in-process probe over its named pipe after an enabled launch |
| [mcp/server.py](../mcp/server.py) | Exposes APMI MCP tools and selects the game by executable and runtime path |
| [vendor/efmi-mcp](../vendor/efmi-mcp) | Source snapshot of the D3D11 probe and Python capture backend |

## UI and launch flow

1. WebView2 serves the embedded UI from `app.apmi.local` and maps wallpapers to `wallpapers.apmi.local`. The host handles messages such as `saveSettings`, `browse`, `start` and `cancel`. Native modal pickers are scheduled through the WPF dispatcher after the web message callback returns.
2. Settings are stored at `data/settings.json` next to the executable. A valid Start Game request writes a settings snapshot to `data/sessions/<timestamp>-<suffix>/request.json` and starts the worker, requesting Windows elevation when needed. The window polls `state.json` and writes a `cancel` marker on cancellation.
3. The worker validates the snapshot and locks on both the canonical game executable and runtime DLL paths. It checks for an already running game before preparing the selected package. It opens the official launcher and waits for a new process with the configured full path and Windows session.
4. The worker checks the exact loaded DLL path and waits for a fresh initialization header with the expected game path. The final status is `initialized`, `failed` or `cancelled`, with exit code 0, 1 or 2 respectively. App startup returns 3 for a missing worker request or an unhandled worker startup error.

When `EnableMcp` is true, the worker loads the probe before the XXMI DLL, then checks the probe's process identity, loaded runtime and rendering attachment. Its status goes to `mcp-status.json` and the final state records `McpReady`. The UI stores the switch with the other settings. Older settings default to false.

The Python MCP host starts `dist/mcp/server.py`. The adapter uses the bundled backend and exposes 34 `apmi_*` tools. A connection must match both configured paths. A change of target or a broken connection clears cached captures. The probe's named pipe uses a PID-specific endpoint and supports capture ownership across clients. Automatic slot collection and the recording hotkey are disabled in the bundled probe configuration.

## Runtime and diagnostics

- The package needs `d3d11.dll`, `d3dcompiler_47.dll`, `d3dx.ini` and either `Core/APMI/main.ini` or `Core/EFMI/main.ini`. The game and DLLs must be x64. Worker timeouts default to 180 seconds for the game process and 45 seconds for initialization.
- Preparation backs up `d3dx.ini` as `d3dx-before.ini`, updates loader and diagnostic settings in the selected folder, and archives any old log as `previous-d3d11_log.txt` in the session directory. Successful verification saves `runtime-initialization.txt` there. `launcher.log` gives stage and operation details, while `state.json` records terminal status and Win32 errors.

## Build and test

- The publish script writes to `dist/`, copies the wallpapers and initializes `dist/APMI/` from `-PackageSource` when absent. `-EfmiSource` is a compatible alias. Subsequent builds reuse the working package and saved settings. `-SelfContained` includes the .NET runtime as separate DLLs. [Build script](../scripts/build.ps1)
- The build first runs `scripts/build-mcp.ps1` with MSVC to compile the bundled probe source, package the Python server, and generate a local `dist/mcp-config.json`.
- `py -3.12 tests/integration.py` runs against the published worker. It uses MSVC to build a DX11 fixture and exercises isolated package copies under `build/tests/<run-id>/`. The normal case checks the mapped DLL and 90 submitted frames while a fixture with the same filename runs at a different path. Other cases cover stale logs, timeouts, cancellation, duplicate launches, missing files and early exit. [Integration suite](../tests/integration.py)
