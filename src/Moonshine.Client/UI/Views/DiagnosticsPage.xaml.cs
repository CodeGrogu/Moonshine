using Microsoft.UI.Xaml.Controls;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        this.InitializeComponent();
        ViewModel = new DiagnosticsViewModel();
    }
}
