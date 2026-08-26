using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Moonshine.UI;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winui_unhandled.log"), $"[UnhandledException]: {e.Message} \nException: {e.Exception}\n");
            e.Handled = true;
        };

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[INFO] App() ctor starting InitializeComponent at {DateTime.UtcNow:O}...\n");
            this.InitializeComponent();
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] App() InitializeComponent succeeded.\n");
        }
        // ALLOWED_EXCEPTION: Log critical initialization crash to disk before rethrowing exception.
        catch (Exception ex)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH in App.ctor]: {ex}\n");
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] OnLaunched entered, initializing AppServices...\n");
            AppServices.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] AppServices initialized, creating MainWindow...\n");
            _mainWindow = new MainWindow();
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] MainWindow created, activating...\n");
            _mainWindow.Activate();
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] MainWindow activated successfully.\n");
        }
        // ALLOWED_EXCEPTION: Log critical launch crash to disk before rethrowing exception.
        catch (Exception ex)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH in OnLaunched]: {ex}\n");
            throw;
        }
    }
}
