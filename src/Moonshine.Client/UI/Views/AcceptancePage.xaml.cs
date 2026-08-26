using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class AcceptancePage : Page
{
    public AcceptanceViewModel ViewModel { get; }

    public AcceptancePage()
    {
        this.InitializeComponent();
        ViewModel = new AcceptanceViewModel(DispatcherQueue.GetForCurrentThread());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
