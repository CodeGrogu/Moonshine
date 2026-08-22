using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace Moonshine.Interop.Tests;

public class NativeAbiLayoutTests
{
    [Fact]
    public void MoonshinePacketDesc_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshinePacketDesc>().Should().Be(32);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.SequenceNumber)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.FrameIndex)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PacketIndex)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.TotalPackets)).ToInt32().Should().Be(10);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PayloadSize)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PacketType)).ToInt32().Should().Be(14);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.Flags)).ToInt32().Should().Be(15);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.BufferSlotIndex)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.StreamPacketIndex)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PayloadPtr)).ToInt32().Should().Be(24);
    }

    [Fact]
    public void MoonshineFrameDesc_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineFrameDesc>().Should().Be(24);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.FrameIndex)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.TotalBytes)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.PacketCount)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.IsKeyframe)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.Reserved)).ToInt32().Should().Be(13);
        Marshal.OffsetOf<MoonshineFrameDesc>(nameof(MoonshineFrameDesc.FrameBuffer)).ToInt32().Should().Be(16);
    }

    [Fact]
    public void MoonshineDecoderCaps_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineDecoderCaps>().Should().Be(20);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.MaxWidth)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.MaxHeight)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.MaxFps)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsAv1)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsHevc)).ToInt32().Should().Be(13);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsH264)).ToInt32().Should().Be(14);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsHdr10)).ToInt32().Should().Be(15);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.Supports10Bit)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsD3D12)).ToInt32().Should().Be(17);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.SupportsVulkan)).ToInt32().Should().Be(18);
        Marshal.OffsetOf<MoonshineDecoderCaps>(nameof(MoonshineDecoderCaps.Reserved)).ToInt32().Should().Be(19);
    }

    [Fact]
    public void MoonshineCaptureFrameDesc_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineCaptureFrameDesc>().Should().Be(36);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.TextureHandle)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.Width)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.Height)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.Format)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.TimestampQpc)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.AccumulatedFrames)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.CursorVisible)).ToInt32().Should().Be(32);
        Marshal.OffsetOf<MoonshineCaptureFrameDesc>(nameof(MoonshineCaptureFrameDesc.Reserved)).ToInt32().Should().Be(33);
    }

    [Fact]
    public void MoonshineHdr10Metadata_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineHdr10Metadata>().Should().Be(32);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.RedPrimary)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.GreenPrimary)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.BluePrimary)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.WhitePoint)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.MaxMasteringLuminance)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.MinMasteringLuminance)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.MaxContentLightLevel)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.MaxFrameAverageLightLevel)).ToInt32().Should().Be(26);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.HdrEnabled)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.ColorSpace)).ToInt32().Should().Be(29);
        Marshal.OffsetOf<MoonshineHdr10Metadata>(nameof(MoonshineHdr10Metadata.Reserved)).ToInt32().Should().Be(30);
    }

    [Fact]
    public void MoonshineEncoderCaps_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineEncoderCaps>().Should().Be(32);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.SupportedCodecsMask)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.MaxWidth)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.MaxHeight)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.MaxFps)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.Supports10Bit)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.SupportsLossless)).ToInt32().Should().Be(17);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.SupportsSmartIdr)).ToInt32().Should().Be(18);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.VendorId)).ToInt32().Should().Be(19);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.MinBitrateKbps)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.MaxBitrateKbps)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineEncoderCaps>(nameof(MoonshineEncoderCaps.Reserved)).ToInt32().Should().Be(28);
    }

    [Fact]
    public void MoonshineEncoderConfig_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineEncoderConfig>().Should().Be(32);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.Width)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.Height)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.Fps)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.BitrateKbps)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.PeakBitrateKbps)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.Codec)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.RcMode)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.GopLength)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.EnableIntraRefresh)).ToInt32().Should().Be(30);
        Marshal.OffsetOf<MoonshineEncoderConfig>(nameof(MoonshineEncoderConfig.EnableFillerData)).ToInt32().Should().Be(31);
    }

    [Fact]
    public void MoonshineEncodedPacketDesc_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineEncodedPacketDesc>().Should().Be(24);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.FrameIndex)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.TimestampQpc)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.PayloadSize)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.IsKeyframe)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.IsHeaderPacket)).ToInt32().Should().Be(21);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.TemporalId)).ToInt32().Should().Be(22);
        Marshal.OffsetOf<MoonshineEncodedPacketDesc>(nameof(MoonshineEncodedPacketDesc.Reserved)).ToInt32().Should().Be(23);
    }

    [Fact]
    public void MoonshineVirtualAudioDriverStatus_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineVirtualAudioDriverStatus>().Should().Be(44);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.IsInstalled)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.IsRenderEndpointPresent)).ToInt32().Should().Be(1);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.IsCaptureEndpointPresent)).ToInt32().Should().Be(2);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.Reserved)).ToInt32().Should().Be(3);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.SupportedSampleRatesCount)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.SupportedChannelsCount)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineVirtualAudioDriverStatus>(nameof(MoonshineVirtualAudioDriverStatus.DriverVersion)).ToInt32().Should().Be(12);
    }

    [Fact]
    public void MoonshineAudioIpcMetrics_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineAudioIpcMetrics>().Should().Be(36);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.RenderPacketsRead)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.RenderUnderruns)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.RenderOverruns)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.CapturePacketsWritten)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.CaptureUnderruns)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.CaptureOverruns)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.SampleRate)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.Channels)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineAudioIpcMetrics>(nameof(MoonshineAudioIpcMetrics.IsConnected)).ToInt32().Should().Be(32);
    }

    [Fact]
    public void MoonshineAdapterInfo_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineAdapterInfo>().Should().Be(160);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.AdapterIndex)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.AdapterLuid)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.Description)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.DedicatedVideoMemory)).ToInt32().Should().Be(140);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.IsHardware)).ToInt32().Should().Be(148);
        Marshal.OffsetOf<MoonshineAdapterInfo>(nameof(MoonshineAdapterInfo.Reserved)).ToInt32().Should().Be(149);
    }

    [Fact]
    public void MoonshineDisplayInfo_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineDisplayInfo>().Should().Be(36);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.DisplayIndex)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.AdapterIndex)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.Width)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.Height)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.RefreshRateNumerator)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.RefreshRateDenominator)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.Rotation)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.IsAttachedToDesktop)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.IsHdr)).ToInt32().Should().Be(29);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.BitsPerColor)).ToInt32().Should().Be(30);
        Marshal.OffsetOf<MoonshineDisplayInfo>(nameof(MoonshineDisplayInfo.Reserved)).ToInt32().Should().Be(31);
    }

    [Fact]
    public void MoonshineSwapchainMetrics_HasExactLayoutAndSize()
    {
        Marshal.SizeOf<MoonshineSwapchainMetrics>().Should().Be(24);
        Marshal.OffsetOf<MoonshineSwapchainMetrics>(nameof(MoonshineSwapchainMetrics.FramesPresented)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineSwapchainMetrics>(nameof(MoonshineSwapchainMetrics.PresentationErrors)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineSwapchainMetrics>(nameof(MoonshineSwapchainMetrics.DroppedFrames)).ToInt32().Should().Be(16);
    }
}
