# APMI Launcher

A Windows x64 launcher for Azur Promilia with APMI Core and the XXMI D3D11 runtime.

The interface uses HTML, CSS and JavaScript embedded in a WebView2 control, with a C# / WPF host on .NET 9. It supports a live video wallpaper, static artwork, saved background preferences, and native file and folder pickers.

Extract the entire folder and run `APMI Launcher.exe` (`dist/APMI Launcher.exe` for local builds). On first launch, all three paths in **Settings** are empty. Select the game executable, official launcher executable and **APMI Package** folder, then save. Select the `APMI` folder next to the launcher or another compatible local package. The package contains `d3d11.dll`, `d3dcompiler_47.dll`, `d3dx.ini` and either `Core/APMI/main.ini` or legacy `Core/EFMI/main.ini`. Select **Start Game**, approve the Windows administrator prompt, then start the game in the official launcher. The APMI window reports launch progress, Windows errors and runtime initialization.

Starting with v0.1.1, the Windows release uses a small EXE with separate application and .NET runtime DLLs. Keep the entire extracted folder together; copying only the EXE will not work. The release includes .NET, while Microsoft Edge WebView2 Runtime is still required.

The launcher matches the configured executable's full path, Windows session, and creation time. It uses a standard remote `LoadLibraryW` call, resolving the function from its actual provider module. It confirms the exact DLL path in the target process, then checks a fresh runtime log for the exact game path. The initialized status describes DLL initialization. A visible 3DMigoto overlay or game frame capture is the additional evidence of graphics interception.

Each attempt writes `data/sessions/<timestamp>/launcher.log`, `state.json`, a settings snapshot, and the previous runtime INI. Existing runtime logs are archived there before a new attempt. A failed Windows operation ends that attempt with its error. The loader leaves an unfinished thread's path allocation alive until the target process exits, avoiding a dangling remote argument.

Version 0.2.0 includes APMI Core 0.2.2 and XXMI 1.3.3. The [APMI Package source repository](https://github.com/loyal37/APMI-Package) also contains frame-dump extraction and Blender import/export tools. Existing EFMI packages are supported for launch and capture workflows.

APMI sets `Rendering.track_region_hashes = 0` in its working copy. XXMI 1.3.3 can create a region-hash cache with only one slot for small buffers; after the first insertion, looking up a second region loops indefinitely. The login-screen frame dump stopped at draw 51, which reuses a 24-byte index buffer at a second offset. The [upstream cache implementation](https://github.com/SpectrumQT/XXMI-Libs-Package/blob/v1.3.3/DirectX11/ResourceHash.h#L35) reproduced the infinite lookup in an isolated test; using a minimum capacity of two avoids it. Disabling region hashes avoids that path while retaining the original dump options and EFMI DLL. Buffer hashes consequently refer to whole buffers.

The workaround was verified in the real game's same login screen on 2026-11-24 at 20:58 Asia/Shanghai: `FrameAnalysis-2026-11-24-205806` completed in approximately three seconds with 1,378 files (98.8 MB). Draw 51's index-buffer dump now succeeds with `first=6,count=6`; the capture reaches frame presentation after draw 70, the runtime reports `Frame analysis saved`, and game PID 30728 remains responsive. The rebuilt launcher also passed the integration suite (`build/tests/b9baa7dc43`). The original stalled log and isolated cache reproduction are preserved in `build/dump-diagnosis`.

## Build

Requires the .NET 9 SDK and the MSVC x64 C++ toolchain with the Windows SDK for the MCP probe. Running a regular build requires the .NET 9 Desktop Runtime and Microsoft Edge WebView2 Runtime. Supply an existing APMI or EFMI package for the first build. Runtime DLLs and local settings are supplied locally.

```powershell
./scripts/build.ps1 -PackageSource 'F:\APMI Package'
```

Use `-PackageSource <folder>` for the initial runtime copy. `-EfmiSource` is retained as an alias. Add `-SelfContained` to build with the .NET runtime included as separate DLLs. Both modes produce a folder containing the EXE and its dependencies. Building again preserves the existing working runtime and saved settings.

The build copies the artwork from `wallpapers/` into `dist/wallpapers/`. WebView2 Runtime is still required for self-contained .NET builds. See [third-party notices](THIRD_PARTY_NOTICES.md) for runtime and artwork attribution.

## Verification

`py -3.12 tests/integration.py` builds an independent DX11 fixture with MSVC and exercises the same worker entry point used by the UI. All test settings, runtime copies and game processes belong to `build/tests`. The test checks initialization with EFMI, wrong-path process matching, early exit, timeouts, cancellation, duplicate startup, and missing input files.

The WebView2 settings interface was checked with Windows UI Automation: the APMI Package path is editable, invalid package paths are rejected without saving, the native folder picker returns the selected directory, and Save persists the path for subsequent use. Native pickers are scheduled after WebView2 message callbacks return to avoid modal dialog reentrancy.

On 2026-11-24, the integration checks passed and the fixture submitted 90 frames through the copied EFMI DLL. The settings panel and save action were also exercised through Windows UI Automation.

The user then launched Azur Promilia through the application. The elevated worker verified the exact EFMI DLL in game PID 23392 at 20:44:02 Asia/Shanghai, followed by its fresh initialization log at 20:44:09. The user confirmed the in-game 3DMigoto debugging text. Evidence is in `dist/data/sessions/20261124-204356-34954cfd`. These checks cover startup, DLL initialization and the graphics overlay.

The GUI calls its worker through `--worker <session-directory>`. The directory must contain `request.json` with the serialized `LauncherSettings`. The worker publishes its final result in `state.json`, returning 0 for verified initialization, 1 for failure, and 2 for cancellation.

## Project memory

When resuming work in a source checkout, start with the [current state](.ai-memory/CURRENT_STATE.md) and [tasks](.ai-memory/TASKS.md). The [project overview](.ai-memory/PROJECT_OVERVIEW.md) links to the other memory documents. Maintenance instructions are in [AGENTS.md](AGENTS.md).

## Live MCP capture

Version 0.2.0 adds **MCP Capture** to Settings. The default is off, including settings saved by earlier versions. Enable it and save before starting the game to load the capture probe. The saved value applies to the next game launch. The worker verifies the probe's game path, runtime path, D3D11 device and swap chain before reporting **APMI + MCP Ready**. Each enabled attempt records the probe status in its session's `mcp-status.json`.

The build bundles the probe and Python server under `dist/mcp/`. An MCP client starts the server over stdio with Python 3.12. Use the local build's `mcp-config.json` or the release's `mcp-config.example.json`. Set the script argument to the absolute path of `mcp/server.py` in your extracted folder.

```powershell
py -3.12 "mcp/server.py"
```

The server reads `dist/data/settings.json` by default. Use `--settings <file>` for another launcher copy and `--pid <pid>` to further restrict the selected process. Connections must match both the configured game executable and the selected package's `d3d11.dll`. Saving different paths updates the server's selection on its next call.

The 34 `apmi_*` tools cover status, frame capture, draw lists, input layouts, IB/VB/CB contents, textures, shader disassembly, texture-slot sampling evidence, binding traces, continuous frame history, triggers, capture comparisons, mod indexing and dump export. Begin with `apmi_status`, then `apmi_capture_frame` and `apmi_list_draws`. Use `contents="buffers"` to retain geometry and constant-buffer data, or `contents="inputs"` to retain textures too. The returned exactness describes whether data belongs to the captured call. Explicit exports go to `dist/data/captures/` by default.

Additional shared D3D11/XXMI workflows are bundled:

| Tools | Use |
| --- | --- |
| `apmi_reload_and_capture`, `apmi_toggle_mods` | Apply edited files through F10 or compare mods on/off through the Core's verified key binding. Report observed input and resulting frames. |
| `apmi_export_mesh` | Export one indexed draw as OBJ, raw buffers and a layout/provenance manifest. Includes shared zero-stride attributes. This is a mesh inspection export; skeleton and material reconstruction are separate work. |
| `apmi_record`, `apmi_record_list`, `apmi_record_history`, `apmi_record_keyframe` | Record frame summaries and keyframes, inspect events and replay saved bindings/constant buffers offline. Use `state="off"` to stop recording. The bundled keyboard hotkey is disabled. |
| `apmi_texture_dhash` | Compute a texture fingerprint from exact retained pixels, with provenance distinct from XXMI's cached value. |
| `apmi_frame_dump_summary`, `apmi_verify_against_dump` | Inspect existing dumps, including deduplicated symbolic links, and compare region or whole-buffer hashes with a live scene. |

Release the capture workspace with `apmi_session(action="release")` after analysis. Probe recordings go under `dist/mcp/dist/recordings/` by default (or `dist/mcp/captures/` when that folder exists). Slot-cache functionality is deferred by request. Game-specific Endfield/ZZMI extraction, merged skeleton, LoD and LoyalTools export tools are not exposed as AP features.

The capture backend comes from the project owner's EFMI MCP source snapshot. Provenance and local integration changes are recorded in `vendor/efmi-mcp/UPSTREAM.json`. The probe reads hash settings from the loaded XXMI runtime. The APMI working package uses whole-buffer hashes. [Third-party notices](THIRD_PARTY_NOTICES.md)

The published worker and MCP transport can be checked with `py -3.12 tests/mcp_integration.py`. It uses private runtime copies and DX11 fixtures to exercise probe injection, matching across two processes, buffer/texture reads, mesh export, fingerprinting, dump export, recording/replay and reconnect. It briefly shows its own test window for F10/F11 checks, including rejection of customized bindings. `py -3.12 tests/mcp_backend.py` checks whole-buffer dump comparisons. `tests/mcp_ui.ps1 -TestRoot <reported-test-folder>` checks the saved switch in the actual WebView2 window.
