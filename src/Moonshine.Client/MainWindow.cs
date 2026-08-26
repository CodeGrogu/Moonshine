using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Moonshine.UI.Views;
using Windows.UI;

namespace Moonshine.UI;

public sealed class MainWindow : Window
{
    private NavigationView MainNav = null!;
    private Frame ContentFrame = null!;
    private TextBlock StatusTextBlock = null!;

    public MainWindow()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] MainWindow ctor starting BuildUi...\n");
            BuildUi();
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), "[INFO] MainWindow BuildUi succeeded.\n");
            this.Title = "Moonshine - Ultra-Low-Latency Streaming Platform (Windows 11)";
        }
        // ALLOWED_EXCEPTION: Log critical UI initialization failure before rethrowing.
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH in MainWindow.ctor]: {ex}\nInnerException: {ex.InnerException}\n");
            throw;
        }
    }

    private void BuildUi()
    {
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30))
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        MainNav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed
        };
        Grid.SetRow(MainNav, 0);

        MainNav.MenuItems.Add(new NavigationViewItem { Content = "Host Server", Tag = "Host", Icon = new SymbolIcon(Symbol.Globe) });
        MainNav.MenuItems.Add(new NavigationViewItem { Content = "Client Streamer", Tag = "Client", Icon = new SymbolIcon(Symbol.Play) });
        MainNav.MenuItems.Add(new NavigationViewItem { Content = "Acceptance Centre", Tag = "Acceptance", Icon = new SymbolIcon(Symbol.Accept) });
        MainNav.MenuItems.Add(new NavigationViewItem { Content = "Hardware Diagnostics", Tag = "Diagnostics", Icon = new SymbolIcon(Symbol.Repair) });

        MainNav.FooterMenuItems.Add(new NavigationViewItem { Content = "Settings", Tag = "Settings", Icon = new SymbolIcon(Symbol.Setting) });

        ContentFrame = new Frame
        {
            Margin = new Thickness(16)
        };
        MainNav.Content = ContentFrame;

        MainNav.SelectionChanged += OnNavigationSelectionChanged;
        MainNav.Loaded += OnNavLoaded;

        rootGrid.Children.Add(MainNav);

        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 37, 37, 38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 51, 51, 51)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        };
        Grid.SetRow(statusBar, 1);

        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StatusTextBlock = new TextBlock
        {
            Text = "Moonshine Engine Ready - Direct3D 11 AVX2 Accelerated",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
            FontSize = 12
        };
        Grid.SetColumn(StatusTextBlock, 0);

        var versionTextBlock = new TextBlock
        {
            Text = "Windows 11 x64 | v0.5.6-alpha",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 119, 119, 119)),
            FontSize = 11
        };
        Grid.SetColumn(versionTextBlock, 1);

        statusGrid.Children.Add(StatusTextBlock);
        statusGrid.Children.Add(versionTextBlock);
        statusBar.Child = statusGrid;

        rootGrid.Children.Add(statusBar);

        this.Content = rootGrid;
    }

    private void OnNavLoaded(object sender, RoutedEventArgs e)
    {
        if (MainNav.MenuItems.Count > 1)
        {
            MainNav.SelectedItem = MainNav.MenuItems[1];
            NavigateTo("Client");
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo("Settings");
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag)
    {
        Type pageType = tag switch
        {
            "Host" => typeof(HostPage),
            "Client" => typeof(ClientPage),
            "Acceptance" => typeof(AcceptancePage),
            "Diagnostics" => typeof(DiagnosticsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(ClientPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[INFO] Navigating to {tag} ({pageType.Name})...\n");
                ContentFrame.Navigate(pageType);
                File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[INFO] Navigation to {tag} succeeded.\n");
            }
            // ALLOWED_EXCEPTION: Log navigation page failure to app log without crashing main window shell.
            catch (Exception ex)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH navigating to {tag}]: {ex}\nInner: {ex.InnerException}\n");
            }
        }
    }
}
