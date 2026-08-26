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

        VideoSwapChainPanel.IsTabStop = true;
        VideoSwapChainPanel.PointerPressed += OnPointerPressed;
        VideoSwapChainPanel.PointerMoved += OnPointerMoved;
        VideoSwapChainPanel.PointerReleased += OnPointerReleased;
        VideoSwapChainPanel.PointerWheelChanged += OnPointerWheelChanged;
        VideoSwapChainPanel.KeyDown += OnKeyDown;
        VideoSwapChainPanel.KeyUp += OnKeyUp;
    }

    public void Dispose()
    {
        _videoPresenter?.Dispose();
        _videoPresenter = null;
    }

    private (int X, int Y) GetScaledCoordinates(Windows.Foundation.Point position)
    {
        double actualWidth = VideoSwapChainPanel.ActualWidth;
        double actualHeight = VideoSwapChainPanel.ActualHeight;

        if (actualWidth <= 0 || actualHeight <= 0)
        {
            return ((int)position.X, (int)position.Y);
        }

        const int targetWidth = 1920;
        const int targetHeight = 1080;

        double normalizedX = Math.Clamp(position.X / actualWidth, 0.0, 1.0);
        double normalizedY = Math.Clamp(position.Y / actualHeight, 0.0, 1.0);

        int scaledX = (int)Math.Round(normalizedX * (targetWidth - 1));
        int scaledY = (int)Math.Round(normalizedY * (targetHeight - 1));

        return (scaledX, scaledY);
    }

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        VideoSwapChainPanel.Focus(Microsoft.UI.Xaml.FocusState.Pointer);
        var pt = e.GetCurrentPoint(VideoSwapChainPanel);
        var (x, y) = GetScaledCoordinates(pt.Position);

        // Map left, right, middle buttons to flags (Left = 1, Right = 2, Middle = 4)
        ushort flags = 0;
        if (pt.Properties.IsLeftButtonPressed) flags |= 1;
        if (pt.Properties.IsRightButtonPressed) flags |= 2;
        if (pt.Properties.IsMiddleButtonPressed) flags |= 4;
        
        ViewModel.SendMouseInput(x, y, 0, 0, flags, true);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(VideoSwapChainPanel);
        var (x, y) = GetScaledCoordinates(pt.Position);
        ViewModel.SendMouseInput(x, y, 0, 0, 0, true);
    }

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(VideoSwapChainPanel);
        var (x, y) = GetScaledCoordinates(pt.Position);
        ViewModel.SendMouseInput(x, y, 0, 0, 0, true);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(VideoSwapChainPanel);
        var (x, y) = GetScaledCoordinates(pt.Position);
        ViewModel.SendMouseInput(x, y, (short)pt.Properties.MouseWheelDelta, 0, 0, true);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        ViewModel.SendKeyboardInput((ushort)e.Key, 0, true, 0);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        ViewModel.SendKeyboardInput((ushort)e.Key, 0, false, 0);
        e.Handled = true;
    }
}
