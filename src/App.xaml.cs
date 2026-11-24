using System.IO;
using System.Windows;

namespace ApmiLauncher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--worker")
        {
            try
            {
                var session = Path.GetFullPath(e.Args[1]);
                if (!File.Exists(Path.Combine(session, "request.json"))) { Shutdown(3); return; }
                Shutdown(await new LauncherSession(session).Run());
            }
            catch { Shutdown(3); }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}
