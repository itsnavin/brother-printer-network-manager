using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BrotherNetwork.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var smoke = args.Contains("--smoke-test", StringComparer.Ordinal);
        var demo = smoke || args.Contains("--demo", StringComparer.Ordinal);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show($"The operation failed: {e.Exception.Message}", "Brother Network Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        var window = new MainWindow(demo, smoke);
        if (smoke)
        {
            window.ContentRendered += async (_, _) =>
            {
                try
                {
                    await window.SmokeTest();
                    Console.WriteLine("UI SMOKE PASS");
                    app.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    app.Shutdown(1);
                }
            };
            // A broken UI must not leave a CI job waiting indefinitely.
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            watchdog.Tick += (_, _) => app.Shutdown(2);
            watchdog.Start();
        }
        return app.Run(window);
    }
}
