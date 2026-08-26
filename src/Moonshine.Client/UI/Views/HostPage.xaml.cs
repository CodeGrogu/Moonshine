using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class HostPage : Page
{
    public HostViewModel ViewModel { get; }

    public HostPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.ViewModel = AppServices.Host;
        this.InitializeComponent();
    }
}
