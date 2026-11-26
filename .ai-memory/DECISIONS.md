# APMI Launcher technical decisions

## D-006 · MCP before model extraction and Core adaptation

On 2026-11-26 the user prioritized MCP so live AP draws can supply shader, vertex, bone and material evidence for the other two goals. The user requested a persisted Settings switch controlling injection on the next game launch. Its default is false for both new and legacy settings.

Reuse the EFMI MCP D3D11 probe and capture modules through a source snapshot with recorded provenance. The APMI adapter selects both the exact game executable and runtime DLL path. It exposes the generic capture tools through `apmi_*` names. The worker verifies rendering attachment after loading the probe. [MCP server](../mcp/server.py), [Provenance](../vendor/efmi-mcp/UPSTREAM.json)

## D-001 · WPF host with embedded WebView2 and a separate launch worker

The WPF host owns Windows dialogs and process startup. Embedded HTML, CSS and JavaScript render the UI without a separate web server. The worker runs from the same EXE and reports progress through the session directory while the window remains responsive. [App entry point](../src/App.xaml.cs), [WPF host](../src/MainWindow.xaml.cs)

## D-002 · Process identity and initialization evidence

Match the canonical game path, Windows session and creation time. Lock by both game and DLL path to protect the target and the package from overlapping starts. Require the exact mapped DLL and a new matching initialization log. The stale-log and same-name fixture cases exercise these checks. [Worker](../src/LauncherSession.cs), [Integration suite](../tests/integration.py)

## D-003 · User-selected working package

All three paths start empty in v0.1.1. The user can select the bundled package or another compatible directory. The worker backs up the INI and archives the previous log before launch so the attempt can be diagnosed from its own files. Tests use package copies under their run directory. [Settings](../src/Settings.cs)

On 2026-11-26, the user kept the original `dist/APMI` path for the new Core. AP Core source and the Blender tools live in `F:\APMI Package`. Launcher validation and MCP F11 support cover both Core layouts.

Core 0.2.2 follows the user's EFMI merged-template example and LoyalTools resource names. The INI uses separate component entry points and draw command lists. Core owns skinning and receives AP bone palettes by native pose-buffer identity. Projects retain component and bone data for future AP merged-skeleton remapping.

## D-004 · Whole-buffer hashes with XXMI 1.3.3

Set `Rendering.track_region_hashes = 0` during runtime preparation to avoid the observed frame-analysis hang. This preserves the existing DLL and dump options and changes buffer hashes to whole-buffer hashes. [Known issues](KNOWN_ISSUES.md#issue-001--xxmi-133-frame-analysis-stall)

## D-005 · Folder-based v0.1.1 distribution

Publish a small executable with separate application and runtime files. A self-contained release includes .NET and depends on Microsoft Edge WebView2 Runtime on the machine. Distribute the full folder together. [Build instructions](../README.md#build)
