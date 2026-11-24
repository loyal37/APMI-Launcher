"""Exercise the published worker against owned DX11 processes and isolated runtime copies."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "dist" / "APMI Launcher.exe"
RUNTIME = ROOT / "dist" / "APMI"
RUN = ROOT / "build" / "tests" / uuid.uuid4().hex[:10]
RUN.mkdir(parents=True)
HIDDEN = subprocess.CREATE_NO_WINDOW
workers = []
cases = []


def compile_fixtures():
    vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    vs = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-property", "installationPath"], text=True).strip()
    vcvars = Path(vs) / "VC/Auxiliary/Build/vcvars64.bat"
    no_op = RUN / "noop.cpp"
    no_op.write_text('#include <windows.h>\nBOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}\n')
    batch = RUN / "compile.cmd"
    commands = [f'@call "{vcvars}" >nul', "@if errorlevel 1 exit /b %errorlevel%"]
    for source, output, dll in [(ROOT / "tests/dx11_fixture.cpp", RUN / "fixture.exe", False), (no_op, RUN / "noop.dll", True)]:
        args = ["cl", "/nologo", "/std:c++17", "/EHsc", "/W4", "/WX", "/O2", "/MT", "/DUNICODE", "/D_UNICODE", "/utf-8", str(source), "/Fe:" + str(output)]
        if dll:
            args += ["/LD"]
        args += ["/link", "user32.lib", "/SUBSYSTEM:WINDOWS"]
        commands += ["@" + subprocess.list2cmdline(args), "@if errorlevel 1 exit /b %errorlevel%"]
    batch.write_text("\r\n".join(commands) + "\r\n", encoding="utf-8")
    subprocess.run(["cmd", "/d", "/c", str(batch)], cwd=RUN, creationflags=HIDDEN, check=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def case(name, mode="normal", timeout=4):
    folder = RUN / name
    folder.mkdir()
    runtime = folder / "EFMI"
    shutil.copytree(RUNTIME, runtime,
                    ignore=shutil.ignore_patterns("FrameAnalysis-*", "3DM-*.dmp", "d3d11_log.txt"))
    for filename in ("official.exe", "APMIDx11Test.exe"):
        shutil.copy2(RUN / "fixture.exe", folder / filename)
    target = folder / "APMIDx11Test.exe"
    (folder / "official.exe.ini").write_text("[Fixture]\ntarget=" + (str(target) if mode != "absent" else "") + "\n", encoding="utf-16")
    (folder / "APMIDx11Test.exe.ini").write_text("[Fixture]\ndelay=1600\n" + ("exit=42\n" if mode == "exit" else "idle=1\n" if mode == "idle" else ""), encoding="utf-16")
    session = folder / "session"
    session.mkdir()
    settings = {"GameExe": str(target), "OfficialLauncher": str(folder / "official.exe"), "RuntimeDirectory": str(runtime), "ProcessTimeoutSeconds": timeout, "InitializationTimeoutSeconds": 3}
    (session / "request.json").write_text(json.dumps(settings), encoding="utf-8")
    cases.append(folder)
    return folder, session, settings


def launch(session):
    worker = subprocess.Popen([str(APP), "--worker", str(session)], creationflags=HIDDEN)
    workers.append(worker)
    return worker


def state(session):
    try:
        return json.loads((session / "state.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def wait_status(session, status, seconds=10):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        found = state(session)
        if found.get("Status") == status:
            return found
        if found.get("Status") == "failed":
            raise AssertionError(json.dumps(found, ensure_ascii=True))
        time.sleep(0.05)
    raise AssertionError("Status timeout: " + json.dumps(state(session), ensure_ascii=True))


def finish(worker, session, expected, operation=None):
    worker.wait(timeout=30)
    observed = state(session)
    assert observed.get("Status") == expected, json.dumps(observed, ensure_ascii=True)
    if operation:
        assert observed.get("Operation") == operation, json.dumps(observed, ensure_ascii=True)
    assert worker.returncode == {"initialized": 0, "failed": 1, "cancelled": 2}[expected]
    print("PASS", session.parent.name, expected, operation or "", flush=True)
    return observed


def cleanup():
    # Only our worker handles and processes whose exact image is inside this run are eligible.
    for worker in workers:
        if worker.poll() is None:
            worker.kill()
            worker.wait()
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.QueryFullProcessImageNameW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint)]
    kernel.TerminateProcess.argtypes = [ctypes.c_void_p, ctypes.c_uint]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    for file in RUN.rglob("target.pid"):
        pid = int(file.read_text())
        handle = kernel.OpenProcess(0x1001, False, pid)
        if not handle:
            continue
        buffer = ctypes.create_unicode_buffer(32768)
        size = ctypes.c_uint(len(buffer))
        if kernel.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
            image = Path(buffer.value).resolve()
            if image.is_relative_to(RUN.resolve()) and image.name == "APMIDx11Test.exe":
                kernel.TerminateProcess(handle, 0)
        kernel.CloseHandle(handle)


def main():
    compile_fixtures()
    source_hash = hashlib.sha256((RUNTIME / "d3dx.ini").read_bytes()).hexdigest()
    wrong, _, _ = case("wrong-path", "idle")
    decoy = subprocess.Popen([str(wrong / "APMIDx11Test.exe")], cwd=wrong, creationflags=HIDDEN)
    (wrong / "target.pid").write_text(str(decoy.pid))
    folder, session, _ = case("normal unicode \u4e2d\u6587")
    outcome = finish(launch(session), session, "initialized")
    assert outcome["GamePid"] != decoy.pid
    end = time.monotonic() + 8
    while not (folder / "frames.txt").exists() and time.monotonic() < end:
        time.sleep(0.1)
    assert int((folder / "frames.txt").read_text()) == 90
    mapped = (folder / "d3d11-path.txt").read_bytes().decode("utf-16le")
    assert Path(mapped).resolve() == (folder / "EFMI/d3d11.dll").resolve(), mapped
    assert "Verified exact DLL path" in (session / "launcher.log").read_text(encoding="utf-8")
    assert (session / "runtime-initialization.txt").exists()
    print("PASS rendering 90 frames through the requested EFMI DLL", flush=True)

    folder, session, settings = case("timeout", "absent", 1)
    finish(launch(session), session, "failed", "ProcessTimeout")

    folder, session, settings = case("cancel-and-lock", "absent", 20)
    first = launch(session)
    wait_status(session, "waiting")
    second = folder / "second-session"
    second.mkdir()
    (second / "request.json").write_text(json.dumps(settings))
    finish(launch(second), second, "failed", "SessionLock")
    (session / "cancel").touch()
    finish(first, session, "cancelled")

    folder, session, settings = case("early-exit", "exit")
    finish(launch(session), session, "failed", "ProcessExit")

    folder, session, settings = case("stale-log", "idle")
    shutil.copy2(RUN / "noop.dll", folder / "EFMI/d3d11.dll")
    (folder / "EFMI/d3d11_log.txt").write_text("D3D11 DLL starting init\nGame path: " + settings["GameExe"] + "\n")
    finish(launch(session), session, "failed", "InitializationTimeout")
    assert (session / "previous-d3d11_log.txt").exists()

    folder, session, settings = case("missing-dll", "absent")
    (folder / "EFMI/d3d11.dll").unlink()
    finish(launch(session), session, "failed")
    assert not (folder / "target.pid").exists()

    folder, session, settings = case("already-running", "idle")
    existing = subprocess.Popen([settings["GameExe"]], cwd=folder, creationflags=HIDDEN)
    (folder / "target.pid").write_text(str(existing.pid))
    finish(launch(session), session, "failed", "AlreadyRunning")
    assert source_hash == hashlib.sha256((RUNTIME / "d3dx.ini").read_bytes()).hexdigest()
    print("All integration checks passed. Evidence:", str(RUN).encode("ascii", "backslashreplace").decode(), flush=True)


if __name__ == "__main__":
    try:
        main()
    finally:
        cleanup()
