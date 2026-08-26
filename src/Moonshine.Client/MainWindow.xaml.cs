using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Moonshine.UI.Views;

namespace Moonshine.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "Moonshine - Ultra-Low-Latency Streaming Platform (Windows 11)";
    }

    private void OnNavLoaded(object sender, RoutedEventArgs e)
    {
        // Default to Client Streamer page
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
            ContentFrame.Navigate(pageType);
        }
    }
}
