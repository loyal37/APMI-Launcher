using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ApmiLauncher;

internal static class McpProbe
{
    internal static async Task<string> WaitForReady(GameProcess game, LauncherSettings settings,
        SafeProcessHandle process, Action checkCancelled)
    {
        var timer = Stopwatch.StartNew();
        var expectedRuntime = Native.CanonicalPath(Path.Combine(settings.RuntimeDirectory, "d3d11.dll"));
        string lastError = "The probe has not reported a D3D11 device and swap chain.";
        while (timer.Elapsed.TotalSeconds < settings.InitializationTimeoutSeconds)
        {
            checkCancelled();
            Native.EnsureAlive(process);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var pipe = new NamedPipeClientStream(".", "efmi-probe-" + game.Id,
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeout.Token);
                var request = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, cmd = "status" });
                var header = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(header, request.Length);
                await pipe.WriteAsync(header, timeout.Token);
                await pipe.WriteAsync(request, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
                await pipe.ReadExactlyAsync(header, timeout.Token);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (length is < 2 or > 1048576 || payloadLength != 0)
                    throw new InvalidDataException("Invalid MCP probe status frame.");
                var bytes = new byte[length];
                await pipe.ReadExactlyAsync(bytes, timeout.Token);
                using var document = JsonDocument.Parse(bytes);
                var status = document.RootElement;
                if (!status.GetProperty("ok").GetBoolean())
                    throw new InvalidDataException("The MCP probe rejected its status request.");
                var executable = status.GetProperty("exe").GetString()!;
                if (status.GetProperty("pid").GetInt32() != game.Id ||
                    !string.Equals(Native.CanonicalPath(executable), Native.CanonicalPath(game.Image), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The MCP probe reported a different game process.");
                var runtime = status.GetProperty("xxmi");
                if (status.GetProperty("attached").GetBoolean() && status.GetProperty("swap_chain").GetBoolean()
                    && runtime.GetProperty("present").GetBoolean()
                    && string.Equals(Native.CanonicalPath(runtime.GetProperty("path").GetString()!),
                        expectedRuntime, StringComparison.OrdinalIgnoreCase))
                    return JsonSerializer.Serialize(status, LauncherSettings.JsonOptions);
            }
            catch (OperationCanceledException) { lastError = "The MCP probe status request timed out."; }
            catch (IOException error) { lastError = error.Message; }
            await Task.Delay(150);
        }
        throw new LaunchFailure("Connecting MCP", "Runtime initialized, but MCP capture is unavailable. " + lastError,
            "McpInitializationTimeout");
    }
}
