using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ApmiLauncher;

internal sealed class LaunchFailure(string stage, string message, string operation, int error = 0) : Exception(message)
{
    internal string Stage { get; } = stage;
    internal string Operation { get; } = operation;
    internal int Error { get; } = error;
}

internal sealed record GameProcess(int Id, long CreatedAt, string Image);
internal sealed record TargetModule(string Path, nint Base);

internal static class Native
{
    private const uint QueryLimited = 0x1000;
    private const uint Synchronize = 0x100000;
    private const uint InjectionAccess = 0x2 | 0x400 | 0x8 | 0x10 | 0x20 | Synchronize;
    private const uint WaitTimeout = 258;

    internal static void RequireX64(string path, bool dll)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new LaunchFailure("Config Check", "Required file not found.", path);
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5A4D)
            throw new LaunchFailure("Config Check", "Not a valid Windows executable or DLL.", path);
        reader.BaseStream.Position = 0x3C;
        var header = reader.ReadInt32();
        if (header < 64 || header > reader.BaseStream.Length - 26)
            throw new LaunchFailure("Config Check", "Corrupted executable header.", path);
        reader.BaseStream.Position = header;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
            throw new LaunchFailure("Config Check", "A 64-bit (x64) executable and DLL are required.", path);
        reader.BaseStream.Position = header + 22;
        var characteristics = reader.ReadUInt16();
        if (((characteristics & 0x2000) != 0) != dll || reader.ReadUInt16() != 0x20B)
            throw new LaunchFailure("Config Check", "Executable/DLL file type mismatch.", path);
    }

    internal static string CanonicalPath(string path)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) ThrowWin32("Config Check", "GetFinalPathNameByHandle");
        var result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + result[8..];
        return result.StartsWith(@"\\?\") ? result[4..] : result;
    }

    internal static GameProcess? FindGame(string expectedPath, long createdAfter)
    {
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expectedPath)))
        {
            using (process)
            {
                try { if (process.SessionId != session) continue; }
                catch (InvalidOperationException) { continue; }
                using var handle = OpenProcess(QueryLimited | Synchronize, false, process.Id);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 87) continue;
                    throw Failure("Waiting", "OpenProcess (query)", error);
                }
                if (WaitForSingleObject(handle, 0) == 0) continue;
                if (!GetProcessTimes(handle, out var created, out _, out _, out _)) ThrowWin32("Waiting", "GetProcessTimes");
                if (created < createdAfter) continue;
                var image = new StringBuilder(32768);
                var capacity = image.Capacity;
                if (!QueryFullProcessImageName(handle, 0, image, ref capacity)) ThrowWin32("Waiting", "QueryFullProcessImageName");
                if (string.Equals(CanonicalPath(image.ToString()), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return new GameProcess(process.Id, created, image.ToString());
            }
        }
        return null;
    }

    internal static SafeProcessHandle OpenGame(GameProcess game)
    {
        var handle = OpenProcess(InjectionAccess, false, game.Id);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw Failure("Loading EFMI", "OpenProcess (load)", error);
        }
        try
        {
            if (!GetProcessTimes(handle, out var created, out _, out _, out _) || created != game.CreatedAt)
                throw new LaunchFailure("Loading EFMI", "The game process exited or changed.", "VerifyProcessIdentity");
            if (!IsWow64Process2(handle, out var machine, out var nativeMachine)) ThrowWin32("Loading EFMI", "IsWow64Process2");
            if (machine != 0 || nativeMachine != 0x8664)
                throw new LaunchFailure("Loading EFMI", "The game process must be x64.", "IsWow64Process2");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void EnsureAlive(SafeProcessHandle process)
    {
        if (WaitForSingleObject(process, 0) != 0) return;
        GetExitCodeProcess(process, out var code);
        throw new LaunchFailure("Game Process", $"Game exited unexpectedly (0x{code:X8}).", "ProcessExit");
    }

    internal static List<TargetModule> Modules(int pid)
    {
        using var snapshot = CreateToolhelp32Snapshot(0x8 | 0x10, (uint)pid);
        if (snapshot.IsInvalid) ThrowWin32("Loading EFMI", "CreateToolhelp32Snapshot");
        var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf<ModuleEntry>() };
        var result = new List<TargetModule>();
        if (!Module32First(snapshot, ref entry)) ThrowWin32("Loading EFMI", "Module32First");
        do { result.Add(new TargetModule(entry.ExePath, entry.BaseAddress)); }
        while (Module32Next(snapshot, ref entry));
        var end = Marshal.GetLastWin32Error();
        if (end != 18) throw Failure("Loading EFMI", "Module32Next", end);
        return result;
    }

    internal static async Task<List<TargetModule>> WaitForModules(GameProcess game, SafeProcessHandle process,
        string? requiredPath = null, string stage = "Loading EFMI")
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            EnsureAlive(process);
            try
            {
                var modules = Modules(game.Id);
                if (requiredPath == null || modules.Any(module => string.Equals(module.Path, requiredPath, StringComparison.OrdinalIgnoreCase)))
                    return modules;
            }
            catch (LaunchFailure error) when (error.Error is 18 or 24 or 299 && watch.Elapsed.TotalSeconds < 3)
            { }
            if (watch.Elapsed.TotalSeconds >= 3)
                throw new LaunchFailure(stage, "Timed out waiting for the system loader module in the game process.", "ResolveLoadLibrary");
            await Task.Delay(30);
        }
    }

    internal static async Task LoadLibrary(GameProcess game, SafeProcessHandle process, string dll, Action<string> diagnostic, string stage = "Loading EFMI")
    {
        var localFunction = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
        if (localFunction == 0) ThrowWin32(stage, "GetProcAddress (LoadLibraryW)");
        if (!GetModuleHandleEx(0x4 | 0x2, localFunction, out var provider)) ThrowWin32(stage, "GetModuleHandleEx");
        var providerPath = new StringBuilder(32768);
        if (GetModuleFileName(provider, providerPath, providerPath.Capacity) == 0) ThrowWin32(stage, "GetModuleFileName");
        var modules = await WaitForModules(game, process, providerPath.ToString(), stage);
        var systemD3D = modules.FirstOrDefault(module => Path.GetFileName(module.Path).Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase));
        if (systemD3D != null) diagnostic("D3D11 already mapped before load: " + systemD3D.Path);
        var remoteProvider = modules.First(module => string.Equals(module.Path, providerPath.ToString(), StringComparison.OrdinalIgnoreCase));
        var remoteFunction = remoteProvider.Base + (localFunction - provider);
        var pathBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(dll) + '\0');
        var allocation = VirtualAllocEx(process, 0, (nuint)pathBytes.Length, 0x3000, 0x04);
        if (allocation == 0) ThrowWin32(stage, "VirtualAllocEx");
        var argumentInUse = false;
        try
        {
            if (!WriteProcessMemory(process, allocation, pathBytes, (nuint)pathBytes.Length, out var written))
                ThrowWin32(stage, "WriteProcessMemory");
            if (written != (nuint)pathBytes.Length) throw Failure(stage, "WriteProcessMemory", 299);
            using var thread = CreateRemoteThread(process, 0, 0, remoteFunction, allocation, 0, out _);
            if (thread.IsInvalid) ThrowWin32(stage, "CreateRemoteThread");
            argumentInUse = true;
            var watch = Stopwatch.StartNew();
            while (true)
            {
                var wait = WaitForSingleObject(thread, 0);
                if (wait == 0) { argumentInUse = false; break; }
                if (wait != WaitTimeout) ThrowWin32(stage, "WaitForSingleObject");
                EnsureAlive(process);
                if (watch.Elapsed.TotalSeconds >= 20)
                    throw new LaunchFailure(stage, "Timed out waiting for DLL loader thread.", "LoadLibraryTimeout");
                await Task.Delay(50);
            }
            EnsureAlive(process);
            if (!GetExitCodeThread(thread, out var exitCode)) ThrowWin32(stage, "GetExitCodeThread");
            diagnostic($"LoadLibrary thread exit value: 0x{exitCode:X8}");
            var fullDll = CanonicalPath(dll);
            var loaded = (await WaitForModules(game, process))
                .Any(module => string.Equals(CanonicalPath(module.Path), fullDll, StringComparison.OrdinalIgnoreCase));
            if (!loaded)
                throw new LaunchFailure(stage, $"DLL {Path.GetFileName(dll)} not found in target process (thread returned 0x{exitCode:X8}).", "VerifyModule");
            diagnostic("Verified exact DLL path in target process: " + fullDll);
        }
        finally
        {
            // A running remote thread retains its argument until the target exits.
            if (!argumentInUse) VirtualFreeEx(process, allocation, 0, 0x8000);
        }
    }

    private static LaunchFailure Failure(string stage, string operation, int error)
    {
        var detail = new Win32Exception(error).Message;
        return new LaunchFailure(stage, $"{operation} failed (Win32 error {error}). {detail}", operation, error);
    }
    private static void ThrowWin32(string stage, string operation) => throw Failure(stage, operation, Marshal.GetLastWin32Error());

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry
    {
        public uint Size, ModuleId, ProcessId, GlobalCount, ProcessCount;
        public nint BaseAddress;
        public uint BaseSize;
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle handle, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(SafeProcessHandle handle, out ushort machine, out ushort nativeMachine);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32First(SafeFileHandle snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32Next(SafeFileHandle snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetModuleHandleEx(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileName(nint module, StringBuilder path, int capacity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateRemoteThread(SafeProcessHandle process, nint attributes, nuint stack, nint start, nint argument, uint flags, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeThread(SafeFileHandle thread, out uint code);
}
