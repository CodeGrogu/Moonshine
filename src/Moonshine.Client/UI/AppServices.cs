using System;
using Microsoft.UI.Dispatching;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI;

public static class AppServices
{
    private static DispatcherQueue? _dispatcher;
    private static HostViewModel? _host;
    private static ClientViewModel? _client;
    private static AcceptanceViewModel? _acceptance;
    private static DiagnosticsViewModel? _diagnostics;
    private static SettingsViewModel? _settings;

    public static void Initialize(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settings = new SettingsViewModel();
        _host = new HostViewModel(_dispatcher);
        _client = new ClientViewModel(_dispatcher);
        _acceptance = new AcceptanceViewModel(_dispatcher);
        _diagnostics = new DiagnosticsViewModel(_dispatcher);
    }

    public static HostViewModel Host => _host ?? throw new InvalidOperationException("AppServices not initialized");
    public static ClientViewModel Client => _client ?? throw new InvalidOperationException("AppServices not initialized");
    public static AcceptanceViewModel Acceptance => _acceptance ?? throw new InvalidOperationException("AppServices not initialized");
    public static DiagnosticsViewModel Diagnostics => _diagnostics ?? throw new InvalidOperationException("AppServices not initialized");
    public static SettingsViewModel Settings => _settings ?? throw new InvalidOperationException("AppServices not initialized");
}
