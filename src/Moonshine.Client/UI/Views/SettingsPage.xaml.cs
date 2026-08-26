using Microsoft.UI.Xaml.Controls;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.InitializeComponent();
        ViewModel = new SettingsViewModel();
    }
}
