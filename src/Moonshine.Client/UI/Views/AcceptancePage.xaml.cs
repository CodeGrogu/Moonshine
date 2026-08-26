using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class AcceptancePage : Page
{
    public AcceptanceViewModel ViewModel { get; }

    public AcceptancePage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.ViewModel = AppServices.Acceptance;
        this.InitializeComponent();
    }
}
