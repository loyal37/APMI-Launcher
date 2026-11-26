using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace ApmiLauncher;

public partial class MainWindow : Window
{
    private const string UiHost = "app.apmi.local";
    private const string WallpaperHost = "wallpapers.apmi.local";
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int WmNclButtonDown = 0x00A1;
    private const nint HtCaption = 0x0002;

    private static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private LauncherSettings settings = new();
    private string? sessionDirectory;
    private string? wallpapersDirectory;
    private string launcherVersion = "0.2.0";
    private string runtimeVersion = "";
    private string? startupConfigError;
    private bool busy;
    private bool closeAfterWork;
    private bool webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        WebView.DefaultBackgroundColor = Color.FromArgb(255, 13, 20, 28);
        launcherVersion = ReadLauncherVersion();
        wallpapersDirectory = FindWallpapersDirectory();
        LoadInitialSettings();
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var darkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        var corner = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }

    private void LoadInitialSettings()
    {
        try
        {
            settings = LauncherSettings.Read(LauncherSettings.SettingsPath);
            settings.Validate();
            runtimeVersion = ReadRuntimeVersion(settings.RuntimeDirectory);
            startupConfigError = null;
        }
        catch (Exception error)
        {
            runtimeVersion = "";
            startupConfigError = error.Message;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(LauncherSettings.DataDirectory, "webview2");
            Directory.CreateDirectory(userDataFolder);
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await WebView.EnsureCoreWebView2Async(environment);
            ConfigureWebView(WebView.CoreWebView2);
            webViewReady = true;
            WebView.CoreWebView2.Navigate($"https://{UiHost}/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Microsoft Edge WebView2 Runtime is required to run APMI Launcher. Please install it and try again.",
                "APMI Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show($"Failed to initialize launcher UI: {error.Message}", "APMI Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void ConfigureWebView(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                !uri.Host.Equals(UiHost, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        };

        core.AddWebResourceRequestedFilter($"https://{UiHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        if (!string.IsNullOrEmpty(wallpapersDirectory) && Directory.Exists(wallpapersDirectory))
        {
            core.SetVirtualHostNameToFolderMapping(
                WallpaperHost,
                wallpapersDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        core.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals(UiHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(path)) path = "index.html";

        var (resourceName, contentType) = path.ToLowerInvariant() switch
        {
            "index.html" => ("ApmiLauncher.ui.index.html", "text/html; charset=utf-8"),
            "styles.css" => ("ApmiLauncher.ui.styles.css", "text/css; charset=utf-8"),
            "app.js" => ("ApmiLauncher.ui.app.js", "application/javascript; charset=utf-8"),
            _ => ("", "")
        };

        if (string.IsNullOrEmpty(resourceName))
        {
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
            stream,
            200,
            "OK",
            $"Content-Type: {contentType}\r\nCache-Control: no-cache");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            switch (type)
            {
                case "ready":
                    SendInit();
                    break;
                case "dragWindow":
                    DragHostWindow();
                    break;
                case "minimize":
                    WindowState = WindowState.Minimized;
                    break;
                case "close":
                    Close();
                    break;
                case "start":
                    _ = StartLaunchAsync();
                    break;
                case "cancel":
                    CancelLaunch();
                    break;
                case "openMods":
                    if (!Path.IsPathFullyQualified(settings.RuntimeDirectory) || !Directory.Exists(settings.RuntimeDirectory))
                    {
                        SendStatus(false, false, "warning", "Check Settings", "Please select the APMI package folder in Settings.", "", true);
                        break;
                    }
                    OpenFolder(Path.Combine(settings.RuntimeDirectory, "Mods"));
                    break;
                case "openLogs":
                    OpenFolder(sessionDirectory ?? Path.Combine(LauncherSettings.DataDirectory, "sessions"));
                    break;
                case "browse":
                    QueueBrowse(doc.RootElement);
                    break;
                case "saveSettings":
                    HandleSaveSettings(doc.RootElement);
                    break;
                case "setBackgroundPreference":
                    HandleBackgroundPreference(doc.RootElement);
                    break;
            }
        }
        catch (Exception error)
        {
            SendStatus(busy, false, "error", "Action Failed", error.Message, "", false);
        }
    }

    private void SendMessageToUi(object payload)
    {
        if (!webViewReady || WebView.CoreWebView2 == null) return;
        var json = JsonSerializer.Serialize(payload, BridgeJsonOptions);
        WebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void SendInit()
    {
        var hasVideo = !string.IsNullOrEmpty(wallpapersDirectory) &&
                       File.Exists(Path.Combine(wallpapersDirectory, "official-launcher-background.mp4"));
        var hasImage = !string.IsNullOrEmpty(wallpapersDirectory) &&
                       File.Exists(Path.Combine(wallpapersDirectory, "official-launcher-background.png"));

        SendMessageToUi(new
        {
            type = "init",
            launcherVersion,
            runtimeVersion,
            settings = SerializeSettings(settings),
            wallpaper = new
            {
                hasVideo,
                hasImage,
                videoUrl = hasVideo ? $"https://{WallpaperHost}/official-launcher-background.mp4" : "",
                imageUrl = hasImage ? $"https://{WallpaperHost}/official-launcher-background.png" : ""
            },
            status = startupConfigError != null
                ? new
                {
                    busy = false,
                    canCancel = false,
                    kind = "warning",
                    stage = "Check Settings",
                    message = startupConfigError,
                    detail = "",
                    configError = true
                }
                : new
                {
                    busy = false,
                    canCancel = false,
                    kind = "idle",
                    stage = "",
                    message = "",
                    detail = "",
                    configError = false
                }
        });
    }

    private void SendStatus(bool isBusy, bool canCancel, string kind, string stage, string message, string detail, bool configError)
    {
        SendMessageToUi(new
        {
            type = "status",
            busy = isBusy,
            canCancel,
            kind,
            stage,
            message,
            detail,
            configError
        });
    }

    private async Task StartLaunchAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            settings.Validate();
            startupConfigError = null;
            sessionDirectory = Path.Combine(
                LauncherSettings.DataDirectory,
                "sessions",
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sessionDirectory);
            settings.Save(Path.Combine(sessionDirectory, "request.json"));
            settings.Save(LauncherSettings.SettingsPath);

            SendStatus(true, true, "working", "Authorizing", "Approve the Windows UAC prompt to open the official launcher.", "", false);

            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
                Verb = LauncherSession.IsElevated() ? "open" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--worker");
            start.ArgumentList.Add(sessionDirectory);

            using var worker = Process.Start(start) ?? throw new IOException("Unable to start the launch worker.");
            string? last = null;
            do
            {
                ApplyState(ref last);
                await Task.Delay(180);
            } while (!worker.HasExited);
            ApplyState(ref last);

            var statePath = Path.Combine(sessionDirectory, "state.json");
            var final = File.Exists(statePath)
                ? JsonSerializer.Deserialize<SessionState>(File.ReadAllText(statePath))
                : null;
            if (final == null || final.Status is not ("failed" or "cancelled" or "initialized"))
                throw new IOException($"Launch worker exited unexpectedly (0x{worker.ExitCode:X8}). Check logs for details.");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            SendStatus(false, false, "warning", "Authorization Cancelled", "Click \"Start Game\" to try again.", "", false);
        }
        catch (LaunchFailure failure)
        {
            var isConfig = failure.Stage == "Config Check";
            SendStatus(false, false, "error", failure.Stage, failure.Message, failure.Operation, isConfig);
        }
        catch (Exception error)
        {
            SendStatus(false, false, "error", "Launch Failed", error.Message, "", false);
        }
        finally
        {
            busy = false;
            if (closeAfterWork) Close();
        }
    }

    private void ApplyState(ref string? last)
    {
        if (sessionDirectory == null) return;
        var path = Path.Combine(sessionDirectory, "state.json");
        if (!File.Exists(path)) return;
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return;
        }

        if (text == last) return;
        var state = JsonSerializer.Deserialize<SessionState>(text);
        if (state == null) return;
        last = text;

        var isRunning = state.Status is "checking" or "waiting" or "loading" or "verifying";
        var canCancel = state.Status is "waiting" or "checking";
        var kind = state.Status switch
        {
            "initialized" => "success",
            "failed" => "error",
            "cancelled" => "warning",
            _ => "working"
        };
        var detail = state.Operation.Length > 0
            ? state.Operation + (state.Win32Error == 0 ? "" : $" · Win32 {state.Win32Error}")
            : "";

        SendStatus(isRunning, canCancel, kind, state.Stage, state.Message, detail, false);
    }

    private void CancelLaunch()
    {
        if (!busy || sessionDirectory == null) return;
        try
        {
            File.WriteAllText(Path.Combine(sessionDirectory, "cancel"), "cancel");
            SendStatus(true, false, "working", "Cancelling", "Stopping the current launch task…", "", false);
        }
        catch
        {
            // Ignore race if worker exited concurrently.
        }
    }

    private void QueueBrowse(JsonElement root)
    {
        var request = root.Clone();
        // Native modal dialogs must open after the WebView2 callback returns.
        _ = Dispatcher.InvokeAsync(() =>
        {
            try { HandleBrowse(request); }
            catch (Exception error)
            {
                SendStatus(busy, false, "error", "Cannot Open Picker", error.Message, "", false);
            }
        });
    }

    private void HandleBrowse(JsonElement root)
    {
        var field = root.TryGetProperty("field", out var f) ? f.GetString() : null;
        if (field == "runtime")
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Select APMI Package Folder",
                Multiselect = false,
                InitialDirectory = Directory.Exists(settings.RuntimeDirectory)
                    ? settings.RuntimeDirectory
                    : AppContext.BaseDirectory
            };
            if (folderDialog.ShowDialog(this) == true)
                SendMessageToUi(new { type = "browseResult", field, path = folderDialog.FolderName });
            return;
        }
        if (field is not ("game" or "official")) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Windows Executable (*.exe)|*.exe",
            CheckFileExists = true,
            Title = field == "game" ? "Select Game Executable (AzurPromilia.exe)" : "Select Official Launcher (launcher.exe)"
        };

        var current = field == "game" ? settings.GameExe : settings.OfficialLauncher;
        var currentDir = Path.GetDirectoryName(current);
        if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
        {
            dialog.InitialDirectory = currentDir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            SendMessageToUi(new { type = "browseResult", field, path = dialog.FileName });
        }
    }

    private void HandleSaveSettings(JsonElement root)
    {
        try
        {
            var gameExe = root.TryGetProperty("gameExe", out var g) ? (g.GetString() ?? "").Trim() : settings.GameExe;
            var officialLauncher = root.TryGetProperty("officialLauncher", out var o) ? (o.GetString() ?? "").Trim() : settings.OfficialLauncher;
            var runtimeDirectory = root.TryGetProperty("runtimeDirectory", out var r) ? (r.GetString() ?? "").Trim() : settings.RuntimeDirectory;
            var staticBackground = root.TryGetProperty("staticBackground", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? s.GetBoolean()
                : settings.StaticBackground;
            var wallpaperSource = root.TryGetProperty("wallpaperSource", out var w) && w.GetString() is "video" or "image"
                ? w.GetString()!
                : settings.WallpaperSource;
            var enableMcp = root.TryGetProperty("enableMcp", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? m.GetBoolean()
                : settings.EnableMcp;

            var updated = settings with
            {
                GameExe = gameExe,
                OfficialLauncher = officialLauncher,
                RuntimeDirectory = runtimeDirectory,
                EnableMcp = enableMcp,
                StaticBackground = staticBackground,
                WallpaperSource = wallpaperSource
            };

            updated.Validate();
            updated.Save(LauncherSettings.SettingsPath);
            settings = updated;
            runtimeVersion = ReadRuntimeVersion(settings.RuntimeDirectory);
            startupConfigError = null;

            SendMessageToUi(new
            {
                type = "settingsResult",
                ok = true,
                error = "",
                runtimeVersion,
                settings = SerializeSettings(settings)
            });

            if (!busy)
            {
                SendStatus(false, false, "idle", "", "", "", false);
            }
        }
        catch (Exception error)
        {
            SendMessageToUi(new
            {
                type = "settingsResult",
                ok = false,
                error = error.Message,
                runtimeVersion,
                settings = SerializeSettings(settings)
            });
        }
    }

    private void HandleBackgroundPreference(JsonElement root)
    {
        var staticBackground = root.TryGetProperty("staticBackground", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? s.GetBoolean()
            : settings.StaticBackground;
        var wallpaperSource = root.TryGetProperty("wallpaperSource", out var w) && w.GetString() is "video" or "image"
            ? w.GetString()!
            : settings.WallpaperSource;

        settings = settings with
        {
            StaticBackground = staticBackground,
            WallpaperSource = wallpaperSource
        };

        try
        {
            settings.Save(LauncherSettings.SettingsPath);
        }
        catch
        {
            // Non-fatal if settings directory cannot be written right now.
        }
    }

    private void DragHostWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        ReleaseCapture();
        _ = SendMessage(hwnd, WmNclButtonDown, HtCaption, 0);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error)
        {
            SendStatus(busy, false, "error", "Cannot Open Folder", error.Message, "", false);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        closeAfterWork = true;
        if (sessionDirectory != null)
        {
            try { File.WriteAllText(Path.Combine(sessionDirectory, "cancel"), "cancel"); }
            catch { /* ignore */ }
        }
        e.Cancel = true;
        SendStatus(true, false, "working", "Stopping Session", "Waiting for the active launch task to finish…", "", false);
    }

    private static object SerializeSettings(LauncherSettings s) => new
    {
        gameExe = s.GameExe,
        officialLauncher = s.OfficialLauncher,
        runtimeDirectory = s.RuntimeDirectory,
        enableMcp = s.EnableMcp,
        staticBackground = s.StaticBackground,
        wallpaperSource = s.WallpaperSource
    };

    private static string ReadLauncherVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.2.0";
    }

    private static string ReadRuntimeVersion(string runtimeDirectory)
    {
        var dll = Path.Combine(runtimeDirectory, "d3d11.dll");
        if (!File.Exists(dll)) return "";
        var productVersion = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
        return string.IsNullOrWhiteSpace(productVersion) ? "APMI" : $"APMI · XXMI {productVersion}";
    }

    private static string? FindWallpapersDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && current != null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "wallpapers");
            if (Directory.Exists(candidate) &&
                (File.Exists(Path.Combine(candidate, "official-launcher-background.mp4")) ||
                 File.Exists(Path.Combine(candidate, "official-launcher-background.png"))))
            {
                return candidate;
            }
            current = current.Parent;
        }
        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
}
