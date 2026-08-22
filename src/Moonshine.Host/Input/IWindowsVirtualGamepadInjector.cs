using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;

namespace Moonshine.Host.Input;

/// <summary>
/// Defines virtual game controller driver integration and state injection operations on Windows.
/// </summary>
public interface IWindowsVirtualGamepadInjector : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether a supported kernel virtual controller bus driver is loaded on the host.
    /// </summary>
    bool IsDriverAvailable { get; }

    /// <summary>
    /// Gets the number of currently active virtual controller slots.
    /// </summary>
    int AllocatedControllerCount { get; }

    /// <summary>
    /// Injects a high-frequency controller state packet into the specified virtual controller slot.
    /// </summary>
    bool UpdateControllerState(byte controllerIndex, in ControllerStatePacket packet);

    /// <summary>
    /// Injects a Moonshine protocol gamepad payload into the specified virtual controller slot.
    /// </summary>
    bool UpdateControllerState(byte controllerIndex, in MoonshineInputGamepadPayload payload);

    /// <summary>
    /// Disconnects all virtual controller targets from the host OS.
    /// </summary>
    void DisconnectAll();
}
