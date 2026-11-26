using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ApmiLauncher;

public sealed record SessionState
{
    public string Status { get; init; } = "waiting";
    public string Stage { get; init; } = "Checking";
    public string Message { get; init; } = "Checking game and EFMI package.";
    public string Operation { get; init; } = "";
    public int Win32Error { get; init; }
    public int GamePid { get; init; }
    public bool Elevated { get; init; }
    public bool McpEnabled { get; init; }
    public bool McpReady { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}

internal sealed class LauncherSession(string directory)
{
    private readonly string directory = Path.GetFullPath(directory);
    private SessionState state = new();
    private string StatePath => Path.Combine(directory, "state.json");

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void Report(string status, string stage, string message, string operation = "", int win32Error = 0)
    {
        state = state with
        {
            Status = status, Stage = stage, Message = message, Operation = operation,
            Win32Error = win32Error, Elevated = IsElevated(), UpdatedAt = DateTimeOffset.Now
        };
        AtomicFile.Write(StatePath, JsonSerializer.Serialize(state, LauncherSettings.JsonOptions));
        Diagnostic($"{stage}: {message} {operation}");
    }

    private void Diagnostic(string text)
    {
        var line = DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine;
        File.AppendAllText(Path.Combine(directory, "launcher.log"), line, new UTF8Encoding(false));
    }

    private void CheckCancelled()
    {
        if (File.Exists(Path.Combine(directory, "cancel"))) throw new OperationCanceledException();
    }

    internal async Task<int> Run()
    {
        Directory.CreateDirectory(directory);
        var gates = new List<Semaphore>();
        try
        {
            Report("checking", "Checking", "Checking game and EFMI package.");
            var settings = LauncherSettings.Read(Path.Combine(directory, "request.json"));
            state = state with { McpEnabled = settings.EnableMcp };
            settings.Validate();
            var gamePath = Native.CanonicalPath(settings.GameExe);
            var dllPath = Native.CanonicalPath(Path.Combine(settings.RuntimeDirectory, "d3d11.dll"));
            foreach (var key in new[] { gamePath, dllPath }.Order(StringComparer.OrdinalIgnoreCase))
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant())));
                var gate = new Semaphore(1, 1, @"Local\APMILauncher." + hash);
                if (!gate.WaitOne(0))
                {
                    gate.Dispose();
                    throw new LaunchFailure("Checking", "Another launch session is already running.", "SessionLock");
                }
                gates.Add(gate);
            }
            CheckCancelled();
            var existing = Native.FindGame(gamePath, 0);
            if (existing != null)
                throw new LaunchFailure("Checking", $"Game is already running (PID {existing.Id}). Close the game and try again.", "AlreadyRunning");
            settings.PrepareRuntime(directory);
            var dllVersion = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
            Diagnostic($"Launcher {typeof(LauncherSession).Assembly.GetName().Version}; elevated={IsElevated()}; XXMI DLL={dllVersion}");
            Diagnostic("Target=" + gamePath);
            Diagnostic("DLL=" + dllPath);
            var startedAfter = DateTime.UtcNow.ToFileTimeUtc();
            Report("waiting", "Waiting", "Start the game in the official launcher.");
            Process.Start(new ProcessStartInfo(settings.OfficialLauncher)
            {
                UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(settings.OfficialLauncher)!
            })?.Dispose();
            GameProcess? game = null;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < settings.ProcessTimeoutSeconds)
            {
                CheckCancelled();
                game = Native.FindGame(gamePath, startedAfter);
                if (game != null) break;
                await Task.Delay(25);
            }
            if (game == null)
                throw new LaunchFailure("Waiting", "Timed out waiting for the game to start. Please try again.", "ProcessTimeout");
            state = state with { GamePid = game.Id };
            Diagnostic($"New game PID={game.Id}; image={game.Image}; created={game.CreatedAt}");
            CheckCancelled();
            Report("loading", "Loading EFMI", $"Game detected (PID {game.Id}). Loading EFMI…");
            using var process = Native.OpenGame(game);
            if (settings.EnableMcp)
            {
                Report("loading", "Loading MCP", "Loading the live capture probe…");
                await Native.LoadLibrary(game, process, LauncherSettings.McpProbePath, Diagnostic, "Loading MCP");
            }
            await Native.LoadLibrary(game, process, dllPath, Diagnostic);
            Report("verifying", "Verifying", "DLL loaded into game. Waiting for initialization log…");
            await WaitForInitialization(settings, process);
            if (settings.EnableMcp)
            {
                Report("verifying", "Connecting MCP", "Waiting for the capture probe to attach to rendering…");
                var status = await McpProbe.WaitForReady(game, settings, process, CheckCancelled);
                AtomicFile.Write(Path.Combine(directory, "mcp-status.json"), status);
                state = state with { McpReady = true };
                Diagnostic("Verified MCP process, runtime path, D3D11 device and swap chain.");
            }
            Report("initialized", settings.EnableMcp ? "APMI + MCP Ready" : "EFMI Ready",
                settings.EnableMcp ? "APMI initialized. Live MCP capture is ready." : "EFMI initialized successfully. Check the in-game overlay.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Report("cancelled", "Cancelled", "Launch session cancelled. Click \"Start Game\" to retry.");
            return 2;
        }
        catch (LaunchFailure failure)
        {
            Report("failed", failure.Stage, failure.Message, failure.Operation, failure.Error);
            return 1;
        }
        catch (Exception exception)
        {
            Report("failed", "Launch Failed", exception.Message, exception.GetType().Name,
                exception is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : 0);
            Diagnostic(exception.ToString());
            return 1;
        }
        finally
        {
            foreach (var gate in gates) { gate.Release(); gate.Dispose(); }
        }
    }

    private async Task WaitForInitialization(LauncherSettings settings, SafeProcessHandle process)
    {
        var log = Path.Combine(settings.RuntimeDirectory, "d3d11_log.txt");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < settings.InitializationTimeoutSeconds)
        {
            CheckCancelled();
            Native.EnsureAlive(process);
            var text = ReadSharedLog(log);
            var gameLine = text.Split('\n').FirstOrDefault(line => line.StartsWith("Game path: ", StringComparison.Ordinal));
            var logGame = gameLine?["Game path: ".Length..].Trim();
            if (text.Contains("D3D11 DLL starting init", StringComparison.Ordinal) && logGame != null
                && File.Exists(logGame) && string.Equals(Native.CanonicalPath(logGame), Native.CanonicalPath(settings.GameExe), StringComparison.OrdinalIgnoreCase))
            {
                AtomicFile.Write(Path.Combine(directory, "runtime-initialization.txt"), text);
                Diagnostic("Verified fresh initialization header and exact game path.");
                return;
            }
            await Task.Delay(150);
        }
        throw new LaunchFailure("Verifying", "DLL loaded, but no matching game initialization log was received. Check logs.", "InitializationTimeout");
    }

    private static string ReadSharedLog(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var buffer = new char[65536];
            return new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
        }
        catch (FileNotFoundException) { return ""; }
        catch (IOException) { return ""; }
    }
}
