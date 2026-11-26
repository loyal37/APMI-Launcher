"""Exercise the published MCP server and worker with private DX11 targets."""
from __future__ import annotations

import json
import ctypes
from ctypes import wintypes
from pathlib import Path
import queue
import re
import shutil
import subprocess
import sys
import threading
import time

import integration as fixture


class McpClient:
    def __init__(self, app: Path, settings: Path, output: Path):
        self.replies = queue.Queue()
        self.next_id = 0
        self.log = (output.parent / (output.name + "-stderr.log")).open("w", encoding="utf-8")
        self.process = subprocess.Popen(
            [sys.executable, "-B", str(app / "mcp/server.py"), "--settings", str(settings), "--out", str(output)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.log,
            text=True, encoding="utf-8", creationflags=fixture.HIDDEN,
        )
        threading.Thread(target=self.read, daemon=True).start()
        result = self.request("initialize", {
            "protocolVersion": "2025-06-18", "capabilities": {},
            "clientInfo": {"name": "apmi-integration", "version": "1"},
        })
        assert result["serverInfo"] == {"name": "apmi-mcp", "version": "0.2.0"}

    def read(self):
        for line in self.process.stdout:
            self.replies.put(json.loads(line))

    def request(self, method, params=None):
        self.next_id += 1
        message = {"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}}
        self.process.stdin.write(json.dumps(message) + "\n")
        self.process.stdin.flush()
        reply = self.replies.get(timeout=20)
        assert reply["id"] == self.next_id and "error" not in reply, reply
        return reply["result"]

    def call(self, name, expect_error=False, **args):
        result = self.request("tools/call", {"name": name, "arguments": args})
        assert bool(result.get("isError")) == expect_error, result
        content = [item["text"] for item in result["content"] if item["type"] == "text"]
        text = "\n".join(content)
        try:
            return json.loads(text)
        except ValueError:
            return text

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
        self.process.stdout.close()
        self.log.close()


def write_settings(path, settings):
    path.write_text(json.dumps(settings), encoding="utf-8")


def show_fixture(pid):
    """Show only our owned fixture for the in-process F10/F11 checks."""
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.EnumWindows.argtypes = [callback, wintypes.LPARAM]
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND] + [ctypes.c_int] * 4 + [wintypes.UINT]
    found = []

    @callback
    def visit(hwnd, _):
        actual = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(actual))
        name = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, name, len(name))
        if actual.value == pid and name.value == "APMIDx11Test":
            found.append(hwnd)
        return True

    user32.EnumWindows(visit, 0)
    assert len(found) == 1, found
    window = found[0]
    user32.SetWindowPos(window, None, 20, 20, 320, 180, 0x14)
    user32.ShowWindow(window, 4)
    return lambda: user32.ShowWindow(window, 0)


def mcp_case(name, frames=3000):
    folder, session, settings = fixture.case(name)
    settings["EnableMcp"] = True
    write_settings(session / "request.json", settings)
    (folder / "APMIDx11Test.exe.ini").write_text(
        f"[Fixture]\ndelay=1600\nframes={frames}\n", encoding="utf-16")
    return folder, session, settings


def main():
    fixture.compile_fixtures()
    app = fixture.RUN / "launcher"
    app.mkdir()
    for file in (fixture.ROOT / "dist").iterdir():
        if file.is_file() and file.suffix in (".exe", ".dll", ".json"):
            shutil.copy2(file, app / file.name)
    shutil.copytree(fixture.ROOT / "dist/mcp", app / "mcp")
    shutil.copytree(fixture.ROOT / "dist/runtimes", app / "runtimes")
    fixture.APP = app / "APMI Launcher.exe"

    folder, session, settings = mcp_case("missing-probe")
    probe = app / "mcp/dist/apmi_probe.dll"
    hidden = probe.with_suffix(".missing")
    probe.rename(hidden)
    try:
        fixture.finish(fixture.launch(session), session, "failed")
        assert not (folder / "target.pid").exists()
    finally:
        hidden.rename(probe)

    folder, session, settings = mcp_case("mcp-enabled")
    outcome = fixture.finish(fixture.launch(session), session, "initialized")
    assert outcome["McpEnabled"] and outcome["McpReady"], outcome
    observed = json.loads((session / "mcp-status.json").read_text(encoding="utf-8"))
    assert observed["pid"] == outcome["GamePid"] and observed["attached"] and observed["swap_chain"]
    assert not observed["xxmi_settings"]["track_region_hashes"]
    assert not observed["slot_cache"]["enabled"], observed
    assert (folder / "probe-loaded.txt").read_text() == "1"

    other, other_session, other_settings = mcp_case("other-package")
    other_outcome = fixture.finish(fixture.launch(other_session), other_session, "initialized")
    assert other_outcome["GamePid"] != outcome["GamePid"]

    settings_path = fixture.RUN / "server-settings.json"
    write_settings(settings_path, settings)
    client = McpClient(app, settings_path, fixture.RUN / "capture")
    evidence = {}
    try:
        tools = client.request("tools/list")["tools"]
        assert len(tools) == 34 and all(t["name"].startswith("apmi_") for t in tools)
        evidence["tool_names"] = [t["name"] for t in tools]
        status = client.call("apmi_status")
        assert status["connected"] and status["probe"]["pid"] == outcome["GamePid"], status
        evidence["status"] = status
        evidence["capture"] = client.call("apmi_capture_frame", contents="inputs", cb_data=True)
        draws = client.call("apmi_list_draws", call="DrawIndexed", min_index_count=3, max_index_count=3)
        evidence["draws"] = draws
        match = re.search(r"^#(\d+)\s+.*DrawIndexed", draws, re.MULTILINE)
        assert match, draws
        draw = int(match[1])
        detail = client.call("apmi_get_draw", draw=draw)
        evidence["draw"] = detail
        assert detail["args"]["index_count"] == 3 and detail["vb"]["vb0"]["stride"] == 20, detail
        assert detail["ib"]["hash"] == detail["ib"]["resource_hash"], detail
        assert "vs-cb0" in detail["constant_buffers"] and "ps-t0" in detail["textures"], detail
        for slot in ("ib", "vb0", "vs-cb0"):
            evidence[slot] = client.call("apmi_read_buffer", draw=draw, slot=slot, count=3)
            assert "exact" in str(evidence[slot]).lower(), evidence[slot]
        shared = client.call("apmi_read_buffer", draw=draw, slot="vb1", count=3)
        assert shared["stride"] == 0 and shared["exact"] and shared["element_count"] == 1, shared
        assert shared["elements"] == [{"COLOR": [1.0, 0.5, 0.25, 1.0]}], shared
        evidence["shared_vertex_attributes"] = shared
        evidence["texture"] = client.call("apmi_read_texture", draw=draw, slot="ps-t0", preview=False)
        evidence["shader"] = client.call("apmi_get_shader", hash=detail["shaders"]["vs"])
        assert "vs_5_0" in str(evidence["shader"]), evidence["shader"]
        evidence["slots"] = client.call("apmi_get_texture_slots", draws=[draw])
        assert "ps-t0" in str(evidence["slots"]), evidence["slots"]
        mesh = client.call("apmi_export_mesh", draw=draw)
        assert mesh["vertices"] == 3 and mesh["triangles"] == 1 and mesh["exact"], mesh
        obj = Path(mesh["obj"]).read_text()
        assert obj.count("\nv ") == 3 and "f 1/1 2/2 3/3" in obj, obj
        mesh_info = json.loads((Path(mesh["folder"]) / "mesh.json").read_text())
        assert mesh_info["vertex_buffers"]["vb1"]["stride"] == 0, mesh_info
        assert mesh["index_data_unchanged_since_capture"], mesh
        evidence["mesh"] = mesh
        fingerprint = client.call("apmi_texture_dhash", draw=draw, slot="ps-t0")
        assert re.fullmatch("[0-9a-f]{16}", fingerprint["dhash"]), fingerprint
        assert fingerprint["source"] == "captured_pixels" and not fingerprint["xxmi_cached"], fingerprint
        evidence["fingerprint"] = fingerprint
        evidence["dump"] = client.call("apmi_dump_frame", first=draw, last=draw,
                                      kinds=["ib", "vb", "cb", "tex"], max_mb=32, max_seconds=10)
        assert not evidence["dump"].get("texture_warnings"), evidence["dump"]

        # Check both runtime controls fail before sending input for customized bindings.
        runtime = Path(settings["RuntimeDirectory"])
        bindings = runtime / ("Core/APMI/KeyBindings.ini" if (runtime / "Core/APMI").is_dir() else "Core/EFMI/KeyBindings.ini")
        original = bindings.read_bytes()
        try:
            bindings.write_bytes(original.replace(b"VK_F11", b"VK_F12"))
            refused = client.call("apmi_toggle_mods", expect_error=True, state="off")
            assert not refused["key_sent"] and "No key was sent" in refused["reason"], refused
            evidence["toggle_binding_refusal"] = refused
        finally:
            bindings.write_bytes(original)
        ini = runtime / "d3dx.ini"
        original = ini.read_bytes()
        try:
            ini.write_bytes(original.replace(b"VK_F10", b"VK_F12"))
            refused = client.call("apmi_reload_and_capture", expect_error=True)
            assert "No key was sent" in str(refused), refused
            evidence["reload_binding_refusal"] = refused
        finally:
            ini.write_bytes(original)

        hide = show_fixture(outcome["GamePid"])
        try:
            off = client.call("apmi_toggle_mods", state="off")
            assert off["mods_enabled"] is False and off["input"][0]["key_down_observed"], off
            evidence["toggle_off"] = off
            on = client.call("apmi_toggle_mods", state="on")
            assert on["mods_enabled"] is True, on
            evidence["toggle_on"] = on
            reload = client.call("apmi_reload_and_capture", settle_ms=250, confirm_wait_ms=500)
            assert reload["reload_requested"] and reload["capture"]["frame"] > reload["barrier_frame"], reload
            evidence["reload"] = reload
        finally:
            hide()

        client.call("apmi_record", state="on")
        time.sleep(0.5)
        stopped = client.call("apmi_record", state="off")
        assert "is off" in stopped and "ERROR" not in stopped, stopped
        evidence["record"] = stopped
        evidence["record_list"] = client.call("apmi_record_list")
        evidence["record_history"] = client.call("apmi_record_history", show="lines", max_share=1)
        assert "DrawIndexed" in evidence["record_history"], evidence["record_history"]
        evidence["record_keyframe"] = client.call("apmi_record_keyframe", at_s=0)
        assert "DrawIndexed" in client.call("apmi_list_draws"), evidence["record_keyframe"]
        client.call("apmi_live_start", every=1)
        time.sleep(0.2)
        evidence["live"] = client.call("apmi_live_now", origin="game", min_index_count=3)
        evidence["history"] = client.call("apmi_live_history", seconds=1)
        assert "DrawIndexed" in str(evidence["live"]), evidence["live"]
        client.call("apmi_live_stop")
        client.call("apmi_session", action="release")

        mismatch = settings | {"RuntimeDirectory": other_settings["RuntimeDirectory"]}
        write_settings(settings_path, mismatch)
        assert not client.call("apmi_status")["connected"], "Connected across a game/package mismatch"
        write_settings(settings_path, other_settings)
        assert client.call("apmi_status")["probe"]["pid"] == other_outcome["GamePid"]
        client.call("apmi_get_draw", expect_error=True, draw=draw)
        client.call("apmi_session", action="release")

        fixture.cleanup()
        time.sleep(0.2)
        assert not client.call("apmi_status")["connected"]
        restart = other / "restart-session"
        restart.mkdir()
        write_settings(restart / "request.json", other_settings)
        restarted = fixture.finish(fixture.launch(restart), restart, "initialized")
        assert client.call("apmi_status")["probe"]["pid"] == restarted["GamePid"]
        evidence["reconnected"] = client.call("apmi_capture_frame", contents="buffers", cb_data=True)
        client.call("apmi_session", action="release")
    finally:
        client.close()
    (fixture.RUN / "mcp-evidence.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print("PASS MCP injection, isolation, capture, mesh, fingerprint, recording, controls and reconnect", flush=True)
    print("Evidence:", fixture.RUN, flush=True)


if __name__ == "__main__":
    try:
        main()
    finally:
        fixture.cleanup()
