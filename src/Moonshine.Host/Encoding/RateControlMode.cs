namespace Moonshine.Host.Encoding;

/// <summary>
/// Hardware Video Encoder Rate Control Mode.
/// </summary>
public enum RateControlMode
{
    ConstantBitrate = 0,
    VariableBitrate = 1,
    ConstrainedQuality = 2
}
