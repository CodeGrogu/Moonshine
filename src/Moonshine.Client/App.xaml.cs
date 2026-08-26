using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Moonshine.UI;

public partial class App : Application
{
    public static App? CurrentApp { get; set; }
    public static Window? MainWindowInstance { get; private set; }

    private Window? _mainWindow;

    public App()
    {
        CurrentApp = this;
        this.UnhandledException += (s, e) =>
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.WriteAllText(Path.Combine(baseDir, "winui_unhandled.log"), $"[UnhandledException]: {e.Message} \nException: {e.Exception}\n");
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH App.UnhandledException]: {e.Message}\n{e.Exception}\n");
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
            MainWindowInstance = new MainWindow();
            _mainWindow = MainWindowInstance;
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
