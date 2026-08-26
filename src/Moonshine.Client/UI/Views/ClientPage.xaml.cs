using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.Controls;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class ClientPage : Page, IDisposable
{
    public ClientViewModel ViewModel { get; }
    private readonly DirectXVideoPresenter _videoPresenter;

    public ClientPage()
    {
        this.InitializeComponent();
        ViewModel = new ClientViewModel(DispatcherQueue.GetForCurrentThread());
        _videoPresenter = new DirectXVideoPresenter(VideoSwapChainPanel);

        ViewModel.SwapChainCreated += (s, swapChainHandle) =>
        {
            _videoPresenter.AttachSwapChain(swapChainHandle);
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
    }

    public void Dispose()
    {
        _videoPresenter.Dispose();
        ViewModel.Dispose();
    }
}
