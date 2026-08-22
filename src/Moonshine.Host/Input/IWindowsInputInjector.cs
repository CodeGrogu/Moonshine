namespace Moonshine.Host.Input;

/// <summary>
/// Defines real native Windows input injection operations for mouse, keyboard, and scroll events.
/// </summary>
public interface IWindowsInputInjector : IDisposable
{
    /// <summary>
    /// Injects high-DPI relative mouse movement deltas into the Windows OS input stream.
    /// </summary>
    bool InjectMouseMove(short deltaX, short deltaY);

    /// <summary>
    /// Injects normalized absolute mouse position into the Windows OS input stream.
    /// </summary>
    bool InjectMouseMoveAbsolute(int x, int y, int screenWidth, int screenHeight);

    /// <summary>
    /// Injects mouse button press or release transitions.
    /// </summary>
    bool InjectMouseButton(byte buttonIndex, bool isDown);

    /// <summary>
    /// Injects vertical or horizontal mouse wheel scroll deltas.
    /// </summary>
    bool InjectMouseScroll(short scrollDelta, bool isHorizontal = false);

    /// <summary>
    /// Injects keyboard key-down or key-up transitions with optional scan code and modifiers.
    /// </summary>
    bool InjectKeyboardKey(short virtualKeyCode, short scanCode, bool isDown, byte modifiers = 0);

    /// <summary>
    /// Releases all currently held keys and mouse buttons to eliminate stuck-key states on session teardown.
    /// </summary>
    /// <returns>The number of release events injected.</returns>
    int ReleaseAllHeldInputs();
}
