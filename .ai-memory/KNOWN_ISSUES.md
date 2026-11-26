# APMI Launcher known issues

## ISSUE-001 · XXMI 1.3.3 frame analysis stall

**Status** Mitigated in runtime preparation.

The real game previously stalled during login-screen frame analysis at draw 51. The diagnosis traced the stall to the region-hash cache for small index buffers. The launcher sets `Rendering.track_region_hashes = 0` during package preparation, which makes hashes refer to whole buffers.

On 2026-11-24 at 20:58 Asia/Shanghai, the same screen produced a completed capture in about three seconds with 1,378 files. The trace passed draw 51 and the runtime logged `Frame analysis saved`. Evidence is in `build/dump-diagnosis/` and `dist/APMI/FrameAnalysis-2026-11-24-205806/`. [README diagnosis](../README.md)

## ISSUE-002 · Native picker reentrancy

**Status** Addressed in the WebView2 bridge.

The host queues native file and folder pickers through WPF's dispatcher so the WebView2 callback can return before a modal dialog opens. The recorded UI Automation check exercised package browsing, validation and saving. [Picker code](../src/MainWindow.xaml.cs), [README verification](../README.md#verification)

## LIMIT-001 · Azur Promilia model compatibility

The active package uses Core 0.2.2. The EFMI template passed Blender round trip and live replacement draws for the current character's `d9cf7935` on 2026-11-26. Core allocates replacement position buffers for the exported vertex count. The private worker verified F10 loading a larger mesh while the native buffers were already allocated. Edited meshes, movement, other characters and LOD transitions require separate real-game verification. [Current state](CURRENT_STATE.md)

## LIMIT-002 · Desktop runtime dependencies

Microsoft Edge WebView2 Runtime is required for the window. Framework-dependent builds also need .NET 9 Desktop Runtime. The v0.1.1 self-contained release carries its .NET dependencies as separate files beside the EXE. [README build instructions](../README.md#build)

## ISSUE-003 · Zero-stride vertex attributes

**Status** Addressed in the bundled capture backend.

AP binds shared vertex attributes with a stride of zero. The inherited reader previously returned an empty decoded list for these bindings. It now reports one constant attribute record. A DX11 fixture verifies shared color values, and real AP frame 25800 verified exact color and UV values from draw 34's `vb3`. [Current state](CURRENT_STATE.md)

## LIMIT-003 · MCP startup and prerequisites

The saved injection switch applies when the game next starts. The probe needs to load before D3D11 device creation. Its IPC and capture attachment are checked by the worker. The external stdio server needs Python 3.12 and the generated client configuration contains local paths. Update those paths when moving the published folder. [MCP instructions](../README.md#live-mcp-capture)

## ISSUE-004 · Early process module readiness

**Status** Addressed on 2026-11-26. Module enumeration can succeed before the target maps the system module providing `LoadLibraryW`. The loader now waits up to three seconds for that exact module while checking process liveness. Local worker and MCP regressions passed after the fix. [Current state](CURRENT_STATE.md)

## ISSUE-005 · Mod-toggle notification includes

**Status** Addressed on 2026-11-26. This Core defines its enabled/disabled text in `GUI.ini`, included from `main.ini`. The backend now follows explicit package includes when reading notification definitions. The private published fixture confirmed F11 off and on from actual captured notification buffers. [Current state](CURRENT_STATE.md)
