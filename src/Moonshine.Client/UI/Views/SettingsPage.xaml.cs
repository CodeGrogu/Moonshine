using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.ViewModel = AppServices.Settings;
        this.InitializeComponent();
    }
}
