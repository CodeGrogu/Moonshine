namespace Moonshine.Host.Encoding;

/// <summary>
/// First-class typed error and execution results returned across video encoder operations.
/// </summary>
public enum EncoderResult
{
    Success = 0,
    NotAvailable = 1,
    UnsupportedCodec = 2,
    UnsupportedFormat = 3,
    InvalidConfiguration = 4,
    DeviceLost = 5,
    ResourceFailure = 6,
    EncoderFailure = 7,
    OutputUnavailable = 8,
    OutputInvalid = 9,
    Timeout = 10
}
