using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Moonshine.UI.Controls;
using Moonshine.UI.ViewModels;

namespace Moonshine.UI.Views;

public sealed partial class ClientPage : Page, IDisposable
{
    public ClientViewModel ViewModel { get; }
    private DirectXVideoPresenter? _videoPresenter;

    public ClientPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.ViewModel = AppServices.Client;
        this.InitializeComponent();

        _videoPresenter = new DirectXVideoPresenter(VideoSwapChainPanel);

        ViewModel.SwapChainCreated += (s, swapChainHandle) =>
        {
            _videoPresenter.AttachSwapChain(swapChainHandle);
        };
    }

    public void Dispose()
    {
        _videoPresenter?.Dispose();
        _videoPresenter = null;
    }
}
