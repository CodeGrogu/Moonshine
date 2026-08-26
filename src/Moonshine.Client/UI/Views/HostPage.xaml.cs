using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class HostPage : Page
{
    public HostViewModel ViewModel { get; }

    public HostPage()
    {
        this.InitializeComponent();
        ViewModel = new HostViewModel(DispatcherQueue.GetForCurrentThread());
    }
}
