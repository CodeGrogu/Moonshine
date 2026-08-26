using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.ViewModel = AppServices.Diagnostics;
        this.InitializeComponent();
    }
}
