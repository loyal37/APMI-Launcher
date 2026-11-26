# APMI Launcher session log

## 2026-11-26 · Launcher v0.2.0 release

The user selected APMI Launcher for publication and requested a separate APMI Package source submission. Package commit `68786e6` contains Core 0.2.2, `d3dx.ini`, upstream GPL-3.0 notices and the standalone tools. A clean, self-contained Launcher build passed the 90-frame worker fixture, all 34 MCP tools, four backend regressions and the WebView2 capture-switch checks. Evidence is under `build/releases/v0.2.0/`.

## 2026-11-26 · EFMI entry points and LoyalTools resource names

Core 0.2.2 was deployed to `dist/APMI` at 13:54. The sample in the APMI Package workspace now uses LoyalTools' merged EFMI template structure, resource names and mesh file names. Core handles the game's palettes and computes current and previous poses. The private published worker fixture with XXMI 1.3.3 passed indexed and instanced draws, exact poses, F10 hot loading a 65-vertex mesh and F11 native fallback in `build/tests/bd243c4c41/`. The worker integration suite rendered 90 frames in `build/tests/9d1bb468cf/`.

AP PID 7776 frame 505651 matched seven compute calls and five replacement draws for `d9cf7935`. Index and vertex attribute buffers matched exactly and the current and previous position buffers had maximum error `1.192e-7`. F11 restored five native draws in frame 505875. After archiving the temporary mod, F10 restored the native scene and Core overlay in frame 506393. See `build/ap-current-character-efmi-0.2.2-final/`. The final pre-deployment backup is `build/deployment-20261126-135413-be1e47/` in the source workspace. Work remains local.

## 2026-11-26 · EFMI-style exports on AP Core 0.2.1

The user chose LoyalTools' ordinary EFMI export conventions, with merged skeletons as follow-up work. The APMI Package exporter now writes resource bindings and draw commands in each `TextureOverride_ComponentN`. The current-character export is 48 lines and the project retains the source component and bone data. The published 0.2.0 worker and XXMI 1.3.3 passed the DX11 native skinning fixture and 90-frame integration check in `build/tests/2d22304475/` and `build/tests/76cb26c32b/`. The 34-tool private MCP regression passed in `build/tests/b169a0e09c/`.

Deployed Core 0.2.1 to `dist/APMI`. AP PID 7776 frame 377101 verified one replacement dispatch with the native shader and five replacement draws for `d9cf7935`. Index and attribute bytes matched and the maximum current pose error was `1.192e-7`. F11 off restored the five native draws in frame 377251. Archived the test mod and reloaded. Frame 377765 verified native drawing and the Core overlay. Evidence is in `build/ap-current-character-efmi-0.2.1/`. The package backup is under `build/deployment-20261126-132637-c40f9d/` in the source workspace. Work remains local.

## 2026-11-26 · AP Core 0.2.0 and current character

Launcher 0.2.0 accepts the new Core and legacy EFMI. Probe 0.1.18-apmi.1 finds AP's F11 binding, and the MCP backend identifies APMI notification text. Private published-worker regressions passed on both packages. The latest AP Core MCP run is `build/tests/d8ae652026/`.

The user required the existing XXMI and LoyalTools conventions after rejecting the verbose prototype. AP Core 0.2.0 uses `main.ini` for shader hooks and `API.ini` for shared drawing commands. It was deployed at the original `dist/APMI` path. The first sample from the older dump yielded no matching custom pair in the changed scene. The user then requested testing the character on screen. Captured MCP frame 182602, extracted `d9cf7935` and verified an unchanged Blender 4.5.7 round trip. Frame 188385 contained one custom skin dispatch and five replacement draws, with maximum current pose error `1.192e-7`. F11 removed replacement mesh draws. Removed the temporary test mod and reloaded while the game stayed open. Evidence is recorded in [Current state](CURRENT_STATE.md).

The user cleared the persistent goal before continuing template work. Recorded the revised LoyalTools plan in the APMI Package workspace. No release was published.

## 2026-11-26 · Shared MCP tools and next scope

Expanded local 0.2.0 from 26 to 34 shared tools. Added reload/mod switching, mesh OBJ/raw export, recording/replay and captured-pixel dHash. The user deferred slot cache, requested generic features only, and explicitly postponed release publication. Fixed whole-buffer dump comparison, export exactness/zero-stride attributes, included GUI notification lookup and a target-process system-module readiness race.

Published worker regression passed in `build/tests/e24da64e1b/`; the 34-tool MCP regression passed in `build/tests/91764f87bb/`, including actual F10/F11 on the owned fixture. Four offline parity tests passed. Real AP verification at 11:45 is in `build/apmi-reuse-20261126/verification.json`: frame 93947 matched the supplied 11:34 dump with 662 comparisons and no mismatches, exported three exact meshes, and recorded/replayed 90 frames plus one keyframe. Recording was stopped and capture ownership released.

The user's active goal now places Core adaptation in the empty `F:\APMI Package` repository before standalone Blender extraction/import/export tests. After those tests pass, present the plan for integrating AP in `E:\loyal tools` before add-on changes. No release, tag or remote publication was created.

## 2026-11-26 · MCP integration and real AP verification

- The user set the three development goals, prioritized MCP, and requested a Settings switch that controls DLL injection. Added `EnableMcp`, defaulting to false for legacy and fresh settings, plus the WebView2 toggle and persistence.
- Built launcher 0.2.0. The worker loads the bundled probe before XXMI when enabled, verifies process/runtime identity and rendering attachment, and saves `mcp-status.json`. The Python stdio server exposes 26 `apmi_*` tools through an EFMI MCP source snapshot with provenance. Packaged CRC and dHash helpers for exports.
- Added exact game/package selection and cleared cached captures on reconnect. Fixed the backend's EOF handling and zero-stride vertex reading. Verified those paths through private fixtures, including two simultaneously running packages and a process restart.
- Final worker suite passed in `build/tests/20f8bd49e2/`. Final MCP suite passed in `build/tests/46c2ec5a93/`. UI checks passed in `build/tests/01a985ee8f/`, including Cancel and persistence of both switch values.
- The first user launch had MCP disabled. Enabled and saved the switch through the launcher UI, and the user restarted AP. PID 26832 reached MCP ready at 11:27 in `dist/data/sessions/20261126-112655-e5b1634e/`.
- Real frame 4677 verified generic capture and live tools. After the user placed a character on screen, frame 11095 yielded 673 draws, 1,235 resources and 257 shaders. Eighteen sampled IB/VB/CB reads across three mesh draws were exact. Frame 25800 verified shared color/UV data for a zero-stride stream. Results are under `build/apmi-character-20261126/`. Released the capture workspace after verification.
- Model extraction with Blender and AP Core adaptation remain the next goals. The capture results describe the observed scene and resources. Component identity, bone interpretation and model round-trip validation belong to that follow-up work.

## 2026-11-26 · Initialize project memory

- Reviewed the WPF host, WebView2 UI, worker, runtime preparation, publish script, integration fixture and Git history.
- Used the seven-document layout from the local EFMI MCP memory as the template. Added the APMI-specific [working agreement](../AGENTS.md) and a source-checkout entry in the [README](../README.md#project-memory).
- Checked existing release result files, historical worker states and frame-dump artifacts. The latest inspected worker log records initialization on 2026-11-26. Inventoried the frame-analysis output dated 11:05. [Current state](CURRENT_STATE.md)
- Verified the memory file set, links and diff. The verification in this session covered documentation and existing evidence.

## 2026-11-24 · Package v0.1.1

- Added the launcher and taskbar icon in `12bc1b0`. Commit `f2dd979` established the separate-DLL publish format and empty startup paths.
- Local extracted-package GUI and worker checks passed. See `build/releases/v0.1.1/gui-result.json`, `build/releases/v0.1.1/integration-result.json` and `build/tests/f3459de239/`.

## 2026-11-24 · Diagnose and verify frame analysis

- Traced the login-screen hang to region hashing in XXMI 1.3.3. Applied whole-buffer hashing in the working package and confirmed the same screen could complete a dump. Worker integration evidence is in `build/tests/b9baa7dc43/`. [Known issues](KNOWN_ISSUES.md#issue-001--xxmi-133-frame-analysis-stall)

## 2026-11-24 · Verify the initial launcher

- UI Automation verified package-path editing, picker selection, validation and persistence. The DX11 fixture rendered 90 frames through the copied runtime.
- The worker verified real-game DLL loading and matching initialization for PID 23392. The user confirmed the 3DMigoto overlay. The initial launcher source was committed as `9f61cec`. [README verification](../README.md#verification)
