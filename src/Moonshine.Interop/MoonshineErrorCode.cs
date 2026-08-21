namespace Moonshine.Interop;

/// <summary>
/// Deterministic error codes across the native C-ABI and managed .NET runtime boundaries.
/// Distinguishes invalid arguments, memory constraints, hardware capability limits, and fatal faults.
/// </summary>
public enum MoonshineErrorCode : int
{
    Success = 0,
    InvalidArgument = -1,
    OutOfMemory = -2,
    UnsupportedHardware = -3,
    DeviceLost = -4,
    BufferTooSmall = -5,
    Timeout = -6,
    TransientBusy = -7,
    UseAfterFree = -8,
    DoubleRelease = -9,
    NotInitialized = -10,
    Fatal = -11
}

public static class MoonshineErrorCodeExtensions
{
    public static bool IsSuccess(this MoonshineErrorCode code) => code == MoonshineErrorCode.Success;
    public static bool IsTransient(this MoonshineErrorCode code) => code is MoonshineErrorCode.TransientBusy or MoonshineErrorCode.Timeout;
    public static bool IsFatal(this MoonshineErrorCode code) => code is MoonshineErrorCode.Fatal or MoonshineErrorCode.UseAfterFree or MoonshineErrorCode.DoubleRelease;
}
