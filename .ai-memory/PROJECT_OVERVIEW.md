# APMI Launcher project overview

APMI Launcher is a Windows x64 utility for starting Azur Promilia through its official launcher and loading a user-selected APMI or legacy EFMI package. It provides configurable paths, launch status, links to the active Mods and logs folders, and a choice of live or static wallpaper. The working package uses APMI Core 0.2.2 with XXMI 1.3.3. Core and Blender tooling source are in `F:\APMI Package`.

The app is a .NET 9 WPF executable with an embedded WebView2 interface. The same executable runs the launch worker. The repository contains the launcher, its UI and assets, the publish script and a DX11 integration fixture. Runtime files are supplied locally.

Version 0.2.0 adds a saved MCP Capture switch and an MCP stdio server for live drawing data. A bundled D3D11 probe records draws and their resources inside the game. The Python adapter selects the configured AP process and package and exposes 34 shared tools for inspection, capture, reload, mesh export and recording.

| Area | Location |
| --- | --- |
| WPF app and Windows launch code | [src](../src) |
| Embedded HTML, CSS and JavaScript | [src/ui](../src/ui) |
| Release publish script | [scripts/build.ps1](../scripts/build.ps1) |
| Worker integration suite and owned DX11 fixture | [tests](../tests) |
| APMI MCP adapter and probe configuration | [mcp](../mcp) |
| Capture backend source and provenance | [vendor/efmi-mcp](../vendor/efmi-mcp) |
| Media files and attribution | [wallpapers](../wallpapers), [third-party notices](../THIRD_PARTY_NOTICES.md) |
| Local publish and session output | `dist/` |
| Local test, release assembly and diagnosis artifacts | `build/` |

## Project memory

| Document | Use |
| --- | --- |
| [Current state](CURRENT_STATE.md) | Read first for the source baseline and recorded verification |
| [Tasks](TASKS.md) | Read next for open work and completed milestones |
| [Architecture](ARCHITECTURE.md) | Locate the UI bridge, worker, settings, runtime and tests |
| [Technical decisions](DECISIONS.md) | Recover the reasons behind existing behavior |
| [Known issues](KNOWN_ISSUES.md) | Find limitations, mitigations and their verification scope |
| [Session log](SESSION_LOG.md) | Trace changes and evidence by date |

Build and usage instructions are in the [README](../README.md). Memory maintenance follows the [working agreement](../AGENTS.md).
