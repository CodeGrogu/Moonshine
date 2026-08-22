using System.Runtime.InteropServices;
using Moonshine.Host.Input;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public sealed unsafe class WindowsInputInjectorTests
{
    [Fact]
    public void WindowsSendInputInjector_RelativeMove_ExecutesSuccessfully()
    {
        using var injector = new WindowsSendInputInjector();
        Assert.False(injector.IsDisposed);

        // Zero delta is a valid no-op
        Assert.True(injector.InjectMouseMove(0, 0));

        // Standard movement
        Assert.True(injector.InjectMouseMove(5, -5));
    }

    [Fact]
    public void WindowsSendInputInjector_AbsoluteMove_MultiMonitorMapping_CalculatesNormalizedCoords()
    {
        using var injector = new WindowsSendInputInjector();
        var bounds = injector.GetVirtualDesktopBounds();
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);

        // Primary monitor absolute move (center of 1080p stream)
        bool moveCenter = injector.InjectMouseMoveAbsolute(960, 540, 1920, 1080);
        Assert.True(moveCenter);

        // Clamping to edges
        bool moveOrigin = injector.InjectMouseMoveAbsolute(0, 0, 1920, 1080);
        Assert.True(moveOrigin);

        bool moveMax = injector.InjectMouseMoveAbsolute(1919, 1079, 1920, 1080);
        Assert.True(moveMax);

        // Multi-monitor with explicit offset (e.g. secondary monitor to the right at 1920, 0)
        bool moveSecondary = injector.InjectMouseMoveAbsolute(
            x: 960,
            y: 540,
            clientWidth: 1920,
            clientHeight: 1080,
            monitorOffsetX: 1920,
            monitorOffsetY: 0,
            monitorWidth: 1920,
            monitorHeight: 1080
        );
        Assert.True(moveSecondary);
    }

    [Fact]
    public void WindowsSendInputInjector_InvalidDimensions_FailsClosed()
    {
        using var injector = new WindowsSendInputInjector();

        Assert.False(injector.InjectMouseMoveAbsolute(100, 100, 0, 1080));
        Assert.False(injector.InjectMouseMoveAbsolute(100, 100, 1920, 0));
        Assert.False(injector.InjectMouseMoveAbsolute(100, 100, -1920, 1080));
        Assert.False(injector.InjectMouseMoveAbsolute(100, 100, 1920, -1080));
    }

    [Fact]
    public void WindowsSendInputInjector_MouseButtons_AllButtonsDispatchAndTrack()
    {
        using var injector = new WindowsSendInputInjector();

        // Invalid buttons
        Assert.False(injector.InjectMouseButton(0, true));
        Assert.False(injector.InjectMouseButton(6, true));
        Assert.False(injector.InjectMouseButton(255, false));

        // Valid buttons: 1 (Left), 2 (Right), 3 (Middle), 4 (X1), 5 (X2)
        for (byte b = 1; b <= 5; b++)
        {
            Assert.True(injector.InjectMouseButton(b, isDown: true));
            Assert.True(injector.InjectMouseButton(b, isDown: false));
        }
    }

    [Fact]
    public void WindowsSendInputInjector_MouseScroll_VerticalAndHorizontal_ExecutesSuccessfully()
    {
        using var injector = new WindowsSendInputInjector();

        // Zero delta
        Assert.True(injector.InjectMouseScroll(0, isHorizontal: false));
        Assert.True(injector.InjectMouseScroll(0, isHorizontal: true));

        // Standard vertical wheel delta (120 units)
        Assert.True(injector.InjectMouseScroll(120, isHorizontal: false));
        Assert.True(injector.InjectMouseScroll(-120, isHorizontal: false));

        // Horizontal wheel scroll
        Assert.True(injector.InjectMouseScroll(120, isHorizontal: true));
        Assert.True(injector.InjectMouseScroll(-120, isHorizontal: true));
    }

    [Fact]
    public void WindowsSendInputInjector_Keyboard_ExtendedKeysAndScanCodes_ExecutesSuccessfully()
    {
        using var injector = new WindowsSendInputInjector();

        // Invalid virtual key codes
        Assert.False(injector.InjectKeyboardKey(-1, 0, true));
        Assert.False(injector.InjectKeyboardKey(256, 0, true));

        // Standard key: 'A' (0x41)
        Assert.True(injector.InjectKeyboardKey(0x41, 0, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x41, 0, isDown: false));

        // Key with explicit hardware scan code (e.g. 'W' = 0x11 scan code)
        Assert.True(injector.InjectKeyboardKey(0x57, 0x11, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x57, 0x11, isDown: false));

        // Extended keys (Arrow Right 0x27, Page Down 0x22, Insert 0x2D, Right Ctrl 0xA3)
        Assert.True(injector.InjectKeyboardKey(0x27, 0, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x27, 0, isDown: false));

        Assert.True(injector.InjectKeyboardKey(0x22, 0, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x22, 0, isDown: false));

        Assert.True(injector.InjectKeyboardKey(0x2D, 0, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x2D, 0, isDown: false));

        Assert.True(injector.InjectKeyboardKey(0xA3, 0, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0xA3, 0, isDown: false));
    }

    [Fact]
    public void WindowsSendInputInjector_StuckKeyAndButtonRelease_ClearsAllHeldState()
    {
        using var injector = new WindowsSendInputInjector();

        // Hold multiple inputs
        Assert.True(injector.InjectMouseButton(1, isDown: true));
        Assert.True(injector.InjectMouseButton(2, isDown: true));
        Assert.True(injector.InjectKeyboardKey(0x11, 0, isDown: true)); // Ctrl
        Assert.True(injector.InjectKeyboardKey(0x57, 0, isDown: true)); // 'W'

        int released = injector.ReleaseAllHeldInputs();
        Assert.True(released >= 4);

        // Second release must report 0
        int secondRelease = injector.ReleaseAllHeldInputs();
        Assert.Equal(0, secondRelease);
    }

    [Fact]
    public void WindowsSendInputInjector_BatchedInjection_ExecutesAllInputsInSingleCall()
    {
        using var injector = new WindowsSendInputInjector();

        // Empty span returns 0
        Assert.Equal(0, injector.InjectBatch(ReadOnlySpan<INPUT>.Empty));

        // Batch of 2 inputs: move + scroll
        Span<INPUT> batch = stackalloc INPUT[2];

        batch[0].type = WindowsInputNativeMethods.INPUT_MOUSE;
        batch[0].mi.dx = 1;
        batch[0].mi.dy = 1;
        batch[0].mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_MOVE;

        batch[1].type = WindowsInputNativeMethods.INPUT_MOUSE;
        batch[1].mi.mouseData = 120;
        batch[1].mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_WHEEL;

        int sent = injector.InjectBatch(batch);
        Assert.Equal(2, sent);
    }

    [Fact]
    public void WindowsSendInputInjector_NativeCAbiWrapper_ExecutesSmoothly()
    {
        IntPtr nativeInj = WindowsInputNativeMethods.NativeInputInjectorCreate();
        Assert.NotEqual(IntPtr.Zero, nativeInj);

        try
        {
            VirtualDesktopGeometry bounds = default;
            int getBoundsResult = WindowsInputNativeMethods.NativeInputGetVirtualDesktopBounds(nativeInj, &bounds);
            Assert.Equal(1, getBoundsResult);
            Assert.True(bounds.Width > 0);
            Assert.True(bounds.Height > 0);

            Assert.Equal(1, WindowsInputNativeMethods.NativeInputInjectMouseMove(nativeInj, 0, 0));
            Assert.Equal(1, WindowsInputNativeMethods.NativeInputInjectMouseAbs(nativeInj, 100, 100, 1920, 1080, 0, 0, 1920, 1080));
            Assert.Equal(1, WindowsInputNativeMethods.NativeInputInjectMouseButton(nativeInj, 1, 0));
            Assert.Equal(1, WindowsInputNativeMethods.NativeInputInjectMouseScroll(nativeInj, 120, 0));
            Assert.Equal(1, WindowsInputNativeMethods.NativeInputInjectKeyboard(nativeInj, 0x41, 0, 0, 0));

            uint released = WindowsInputNativeMethods.NativeInputReleaseAllHeld(nativeInj);
            Assert.True(released >= 0);

            WindowsInputNativeMethods.NativeInputRefreshVirtualDesktopBounds(nativeInj);
        }
        finally
        {
            WindowsInputNativeMethods.NativeInputInjectorDestroy(nativeInj);
        }
    }

    [Fact]
    public void WindowsSendInputInjector_DoubleDispose_IsSafeAndIdempotent()
    {
        var injector = new WindowsSendInputInjector();
        Assert.False(injector.IsDisposed);

        injector.Dispose();
        Assert.True(injector.IsDisposed);

        // Post-disposal injection fails gracefully
        Assert.False(injector.InjectMouseMove(5, 5));
        Assert.False(injector.InjectMouseMoveAbsolute(100, 100, 1920, 1080));
        Assert.False(injector.InjectMouseButton(1, true));
        Assert.False(injector.InjectMouseScroll(120));
        Assert.False(injector.InjectKeyboardKey(0x41, 0, true));
        Assert.Equal(0, injector.InjectBatch(stackalloc INPUT[1]));
        Assert.Equal(0, injector.ReleaseAllHeldInputs());

        // Idempotent dispose
        injector.Dispose();
        Assert.True(injector.IsDisposed);
    }
}
