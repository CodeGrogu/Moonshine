using Moonshine.Interop;

namespace Moonshine.Host.Input;

/// <summary>
/// Defines real native Windows input injection operations for mouse, keyboard, and scroll events.
/// </summary>
public interface IWindowsInputInjector : IDisposable
{
    /// <summary>
    /// Gets whether the injector has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Injects high-DPI relative mouse movement deltas into the Windows OS input stream.
    /// </summary>
    bool InjectMouseMove(short deltaX, short deltaY);

    /// <summary>
    /// Injects normalized absolute mouse position mapped across multi-monitor virtual desktop coordinates.
    /// </summary>
    bool InjectMouseMoveAbsolute(
        int x,
        int y,
        int clientWidth,
        int clientHeight,
        int monitorOffsetX = 0,
        int monitorOffsetY = 0,
        int monitorWidth = 0,
        int monitorHeight = 0
    );

    /// <summary>
    /// Injects mouse button press or release transitions.
    /// </summary>
    bool InjectMouseButton(byte buttonIndex, bool isDown);

    /// <summary>
    /// Injects vertical or horizontal mouse wheel scroll deltas.
    /// </summary>
    bool InjectMouseScroll(short scrollDelta, bool isHorizontal = false);

    /// <summary>
    /// Injects keyboard key-down or key-up transitions with scan code translation and extended key flags.
    /// </summary>
    bool InjectKeyboardKey(short virtualKeyCode, short scanCode, bool isDown, byte modifiers = 0);

    /// <summary>
    /// Injects a batched span of pre-constructed Win32 INPUT structures in a single OS call.
    /// </summary>
    int InjectBatch(ReadOnlySpan<INPUT> inputs);

    /// <summary>
    /// Releases all currently held keys and mouse buttons to eliminate stuck-key states on session teardown.
    /// </summary>
    /// <returns>The number of release events injected.</returns>
    int ReleaseAllHeldInputs();

    /// <summary>
    /// Retrieves the current virtual desktop geometry bounding box.
    /// </summary>
    VirtualDesktopGeometry GetVirtualDesktopBounds();

    /// <summary>
    /// Refreshes the virtual desktop geometry from Win32 system metrics.
    /// </summary>
    void RefreshVirtualDesktopBounds();
}
