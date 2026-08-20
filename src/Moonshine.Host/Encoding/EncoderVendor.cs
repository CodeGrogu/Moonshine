namespace Moonshine.Host.Encoding;

/// <summary>
/// Hardware Video Encoder Vendor.
/// </summary>
public enum EncoderVendor
{
    Auto = 0,
    NvidiaNvenc = 1,
    AmdAmf = 2,
    IntelQuickSync = 3,
    Direct3D11Hardware = 4
}
