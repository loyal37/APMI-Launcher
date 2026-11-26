"""APMI MCP entry point using the bundled EFMI MCP D3D11 backend."""
from __future__ import annotations

import argparse
from dataclasses import replace
import json
import os
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
BACKEND = HERE / "backend" / "server"
if not BACKEND.is_dir():
    BACKEND = HERE.parent / "vendor" / "efmi-mcp" / "server"
sys.path.insert(0, str(BACKEND))

from efmi_mcp import __version__ as backend_version
from efmi_mcp import tools as backend
from efmi_mcp.mcp_stdio import ToolError
from efmi_mcp.probe_client import ProbeClient, ProbeError, find_probes

VERSION = "0.2.0"
INSTRUCTIONS = """Live D3D11 data from Azur Promilia launched with APMI Launcher.
Enable MCP Capture in launcher Settings and start the game. The probe is loaded
at startup. Connections must match both the configured game executable and
the exact d3d11.dll in the selected APMI package.

Use apmi_status, apmi_capture_frame, apmi_list_draws, apmi_get_draw, then
apmi_read_buffer / apmi_read_texture / apmi_get_shader. Capture contents='buffers'
keeps geometry and constant-buffer data as each call saw it. contents='inputs'
also keeps textures. Query per-call exactness before using data for extraction.
AP's working package uses whole-buffer hashes with track_region_hashes=0.
The probe reports the actual runtime hash settings.

apmi_live_start and apmi_live_history summarize frames over time.
apmi_session(action='release') releases capture ownership after analysis.
Only the latest capture retains GPU resources. A reconnect clears this server's
old captures. apmi_dump_frame exports captured contents into the output directory.
apmi_get_texture_slots derives sampling evidence from shaders.
apmi_export_mesh writes one draw's OBJ and raw buffers for inspection.
apmi_reload_and_capture applies edited mod files with the installation's F10.
apmi_toggle_mods uses the verified Core binding; restore mods after comparisons.
apmi_record controls recording; record_list/history/keyframe read it offline.
apmi_texture_dhash fingerprints retained texture pixels.
AP merged-skeleton extraction, LoD mapping and mod generation require separate
pipeline adaptation; the Endfield/ZZMI model workflow is not an AP exporter.
"""

EXPOSED_TOOLS = (
    "capture_frame", "list_draws", "get_draw", "find_hash",
    "read_buffer", "read_texture", "get_shader", "get_texture_slots",
    "list_writes", "dump_frame", "capture_draw_outputs", "binding_trace",
    "live_start", "live_stop", "live_now", "watch_hash", "live_history",
    "trigger_arm", "trigger_wait", "trigger_cancel",
    "diff_captures", "list_mods", "frame_dump_summary", "verify_against_dump", "session",
    "reload_and_capture", "toggle_mods", "export_mesh",
    "record", "record_list", "record_history", "record_keyframe",
    "texture_dhash",
)

DESCRIPTIONS = {
    "toggle_mods": "Switch mods on/off/toggle through the selected Core's verified key binding and capture "
                   "the next frame. APMI Core and the legacy EFMI package use F11. Restores focus after input; "
                   "refuses ambiguous/custom bindings or held modifiers. Reports the observed mods_enabled "
                   "state, which can be unknown. Restore mods after comparison. Reloading also enables mods.",
    "record": "Start, stop or inspect a probe recording. Saves per-frame draw summaries and keyframes with "
              "bindings and constant buffers. Read them offline with apmi_record_list, apmi_record_history "
              "and apmi_record_keyframe. Default folder: recordings next to the probe DLL (or its sibling "
              "captures folder if present). The bundled configuration disables the keyboard hotkey. "
              "Recording continues until stopped, the configured limit is reached, or the game exits.",
}


def same_path(left: str | Path, right: str | Path) -> bool:
    return bool(left and right) and os.path.normcase(str(Path(left).resolve())) == os.path.normcase(str(Path(right).resolve()))


class ApmiSession(backend.Session):
    def __init__(self, settings_path: Path, out_dir: Path, probe_dll: Path, pid: int | None = None):
        self.settings_path = settings_path
        self.target_pid = pid
        self.game_exe = ""
        self.enable_mcp = False
        super().__init__(settings_path.parent.parent / "APMI", out_dir, probe_dll)

    def refresh_settings(self) -> None:
        try:
            settings = json.loads(self.settings_path.read_text(encoding="utf-8-sig"))
            game = settings.get("GameExe", "")
            runtime = settings.get("RuntimeDirectory", "")
            if not isinstance(game, str) or not isinstance(runtime, str):
                raise ValueError("GameExe and RuntimeDirectory must be paths.")
            if not game or not runtime or not Path(game).is_absolute() or not Path(runtime).is_absolute():
                raise ValueError("Select the game and APMI package paths in launcher Settings.")
        except (OSError, ValueError, AttributeError) as error:
            self.disconnect()
            raise ToolError(f"Cannot read launcher settings at {self.settings_path}. {error}") from error
        if not same_path(self.game_exe, game) or not same_path(self.configured_root, runtime):
            self.disconnect()
            self.configured_root = Path(runtime)
            self.game_exe = game
            self._installs.clear()
            self._mod_caches.clear()
        self.enable_mcp = bool(settings.get("EnableMcp", False))

    def disconnect(self) -> None:
        if self.client is not None:
            self.client.close()
        self.client = None
        self.captures.clear()
        self.latest = None
        self._shader_cache.clear()
        self._shader_client = None
        self.mods_display = None

    def matches(self, status: dict) -> bool:
        runtime = status.get("xxmi") or {}
        return (
            (self.target_pid is None or status.get("pid") == self.target_pid)
            and same_path(status.get("exe", ""), self.game_exe)
            and runtime.get("present", False)
            and same_path(runtime.get("path", ""), self.configured_root / "d3d11.dll")
        )

    def probe(self) -> ProbeClient:
        self.refresh_settings()
        if self.client is not None and self.client.connected:
            return self.client
        self.disconnect()
        for endpoint in reversed(find_probes()):
            if self.target_pid is not None and endpoint.pid != self.target_pid:
                continue
            candidate = ProbeClient(endpoint)
            try:
                candidate.open()
                status = candidate.status()
                if self.matches(status):
                    self.client = candidate
                    return candidate
            except (OSError, ProbeError):
                pass
            candidate.close()
        raise ToolError(
            "No capture probe matches the configured AP game and package. "
            "Enable MCP Capture in APMI Launcher Settings, save, and start the game. "
            "The probe must load before the D3D11 device is created."
        )

    def call(self, fn, *args, **kwargs):
        client = self.probe()
        try:
            return fn(client, *args, **kwargs)
        except (ProbeError, OSError) as error:
            if not client.connected or isinstance(error, OSError):
                self.disconnect()
            raise ToolError(str(error)) from error


def build_server(settings_path: Path, out_dir: Path, probe_dll: Path, pid: int | None = None):
    session = ApmiSession(settings_path, out_dir, probe_dll, pid)
    server = backend.build_server(session.configured_root, out_dir, probe_dll, session=session)
    original_tools = server.tools
    names = {"efmi_" + name: "apmi_" + name for name in EXPOSED_TOOLS}

    def translate(value):
        if isinstance(value, str):
            for original, renamed in names.items():
                value = value.replace(original, renamed)
            return value
        if isinstance(value, list):
            return [translate(item) for item in value]
        if isinstance(value, dict):
            return {key: translate(item) for key, item in value.items()}
        return value

    def adapt(fn):
        def invoke(**arguments):
            session.refresh_settings()
            try:
                return translate(fn(**arguments))
            except ToolError as error:
                raise ToolError(translate(str(error))) from error
        return invoke

    server.name = "apmi-mcp"
    server.version = VERSION
    server.instructions = INSTRUCTIONS
    server.tools = {
        renamed: replace(original_tools[original], name=renamed,
                         description=DESCRIPTIONS.get(original[5:], translate(original_tools[original].description)),
                         schema=translate(original_tools[original].schema),
                         fn=adapt(original_tools[original].fn))
        for original, renamed in names.items()
    }

    @server.tool("apmi_status", "Show the configured AP target, matching probe, rendering attachment and capture capabilities.")
    def status():
        result = {
            "launcher_settings": str(settings_path), "server_version": VERSION,
            "capture_backend_version": backend_version,
            "probe_dll": str(probe_dll), "output_directory": str(out_dir),
        }
        try:
            session.refresh_settings()
            result.update(game=session.game_exe, package=str(session.configured_root),
                          enabled_for_next_launch=session.enable_mcp)
            result["probe"] = session.call(lambda client: client.status())
            result["connected"] = True
        except ToolError as error:
            result.update(connected=False, message=str(error))
        return result

    return server, session


def main() -> int:
    parser = argparse.ArgumentParser(description="Live AP drawing data over MCP stdio.")
    published = HERE.parent if (HERE / "backend").is_dir() else HERE.parent / "dist"
    parser.add_argument("--settings", type=Path, default=published / "data" / "settings.json")
    parser.add_argument("--out", type=Path, default=published / "data" / "captures")
    parser.add_argument("--probe-dll", type=Path, default=published / "mcp" / "dist" / "apmi_probe.dll")
    parser.add_argument("--pid", type=int, help="Further restrict connections to this game PID.")
    args = parser.parse_args()
    server, session = build_server(args.settings.resolve(), args.out.resolve(), args.probe_dll.resolve(), args.pid)
    print(f"apmi-mcp {VERSION}, {len(server.tools)} tools", file=sys.stderr)
    try:
        server.serve()
    finally:
        session.disconnect()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
