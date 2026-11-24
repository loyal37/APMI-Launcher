# APMI Launcher

A Windows x64 launcher for Azur Promilia, using a local copy of EFMI 1.5.1 and XXMI's D3D11 runtime 1.3.3.

The interface uses HTML, CSS and JavaScript embedded in a WebView2 control, with a C# / WPF host on .NET 9. It supports a live video wallpaper, static artwork, saved background preferences, and native file and folder pickers.

Run `dist/APMI Launcher.exe`, open **Settings**, and select the game executable, official launcher executable and **APMI Package** folder. The package folder contains `d3d11.dll`, `d3dcompiler_47.dll`, `d3dx.ini` and `Core/EFMI/main.ini`. The default working copy is `dist/APMI`; an existing EFMI package directory can also be selected. Select **Start Game**, approve the Windows administrator prompt, then start the game in the official launcher. The APMI window reports launch progress, Windows errors and runtime initialization.

The launcher matches the configured executable's full path, Windows session, and creation time. It uses a standard remote `LoadLibraryW` call, resolving the function from its actual provider module. It confirms the exact DLL path in the target process, then checks a fresh runtime log for the exact game path. The initialized status describes DLL initialization. A visible 3DMigoto overlay or game frame capture is the additional evidence of graphics interception.

Each attempt writes `data/sessions/<timestamp>/launcher.log`, `state.json`, a settings snapshot, and the previous runtime INI. Existing runtime logs are archived there before a new attempt. A failed Windows operation ends that attempt with its error. The loader leaves an unfinished thread's path allocation alive until the target process exits, avoiding a dangling remote argument.

The EFMI package contains Endfield-specific rendering rules. Its inclusion follows the user's choice for loader diagnosis. Azur Promilia model compatibility requires its own verification.

APMI sets `Rendering.track_region_hashes = 0` in its working copy. XXMI 1.3.3 can create a region-hash cache with only one slot for small buffers; after the first insertion, looking up a second region loops indefinitely. The login-screen frame dump stopped at draw 51, which reuses a 24-byte index buffer at a second offset. The [upstream cache implementation](https://github.com/SpectrumQT/XXMI-Libs-Package/blob/v1.3.3/DirectX11/ResourceHash.h#L35) reproduced the infinite lookup in an isolated test; using a minimum capacity of two avoids it. Disabling region hashes avoids that path while retaining the original dump options and EFMI DLL. Buffer hashes consequently refer to whole buffers.

The workaround was verified in the real game's same login screen on 2026-11-24 at 20:58 Asia/Shanghai: `FrameAnalysis-2026-11-24-205806` completed in approximately three seconds with 1,378 files (98.8 MB). Draw 51's index-buffer dump now succeeds with `first=6,count=6`; the capture reaches frame presentation after draw 70, the runtime reports `Frame analysis saved`, and game PID 30728 remains responsive. The rebuilt launcher also passed the integration suite (`build/tests/b9baa7dc43`). The original stalled log and isolated cache reproduction are preserved in `build/dump-diagnosis`.

## Build

Requires the .NET 9 SDK. Running a regular build requires the .NET 9 Desktop Runtime and Microsoft Edge WebView2 Runtime. Supply an existing EFMI package for the first build; runtime DLLs and local settings are not stored in this repository.

```powershell
./scripts/build.ps1 -EfmiSource 'C:\XXMI Launcher\EFMI'
```

Use `-EfmiSource <folder>` for the initial runtime copy, or `-SelfContained` to publish a portable build with .NET included. Building again preserves the existing working runtime.

The build copies the artwork from `wallpapers/` into `dist/wallpapers/`. WebView2 Runtime is still required for self-contained .NET builds. See [third-party notices](THIRD_PARTY_NOTICES.md) for runtime and artwork attribution.

## Verification

`py -3.12 tests/integration.py` builds an independent DX11 fixture with MSVC and exercises the same worker entry point used by the UI. All test settings, runtime copies and game processes belong to `build/tests`. The test checks initialization with EFMI, wrong-path process matching, early exit, timeouts, cancellation, duplicate startup, and missing input files.

The WebView2 settings interface was checked with Windows UI Automation: the APMI Package path is editable, invalid package paths are rejected without saving, the native folder picker returns the selected directory, and Save persists the path for subsequent use. Native pickers are scheduled after WebView2 message callbacks return to avoid modal dialog reentrancy.

On 2026-11-24, the integration checks passed and the fixture submitted 90 frames through the copied EFMI DLL. The settings panel and save action were also exercised through Windows UI Automation.

The user then launched Azur Promilia through the application. The elevated worker verified the exact EFMI DLL in game PID 23392 at 20:44:02 Asia/Shanghai, followed by its fresh initialization log at 20:44:09. The user confirmed the in-game 3DMigoto debugging text. Evidence is in `dist/data/sessions/20261124-204356-34954cfd`. These checks cover startup, DLL initialization and the graphics overlay.

The GUI calls its worker through `--worker <session-directory>`. The directory must contain `request.json` with the serialized `LauncherSettings`. The worker publishes its final result in `state.json`, returning 0 for verified initialization, 1 for failure, and 2 for cancellation.
