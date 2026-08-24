namespace Moonshine.Host.Encoding;

/// <summary>
/// Defines the explicit runtime lifecycle state of an active video encoder backend.
/// </summary>
public enum EncoderRuntimeState
{
    Uninitialised = 0,
    Initialising = 1,
    Ready = 2,
    Encoding = 3,
    Faulted = 4,
    Disposed = 5
}
