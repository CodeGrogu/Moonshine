using System.Diagnostics;
using FluentAssertions;
using Moonshine.Core.Control;
using Moonshine.Core.Security;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineRemoteHostControlClientTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_FormatsRequestAndResolvesOnMatchingResponse()
    {
        byte[]? sentBytes = null;
        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x1234);

        Task<MoonshineHostCapabilitiesResponsePayload> requestTask = client.GetCapabilitiesAsync(queryMask: 0x0F);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 4);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.GetHostCapabilities);
        header.SessionId.Should().Be(0x1234);

        MoonshineErrorCode maskErr = MoonshineProtocolCodec.TryReadGetHostCapabilities(sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize), out uint parsedMask);
        maskErr.Should().Be(MoonshineErrorCode.Success);
        parsedMask.Should().Be(0x0F);

        // Synthesize response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 32];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostCapabilitiesResponse,
            PayloadSize: 32,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x1234,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var expectedPayload = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)(MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc),
            SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
            MaxEncodeWidth = 3840,
            MaxEncodeHeight = 2160,
            MaxEncodeFps = 144,
            SupportsHdr10 = 1,
            SupportsVirtualAudio = 1,
            SupportsMicBackchannel = 1,
            Reserved = 0,
            MaxBitrateKbps = 100000,
            Reserved2 = 0
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(in expectedPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        MoonshineHostCapabilitiesResponsePayload result = await requestTask;
        result.SupportedVideoCodecs.Should().Be(expectedPayload.SupportedVideoCodecs);
        result.MaxEncodeWidth.Should().Be(3840);
        result.MaxEncodeFps.Should().Be(144);
        result.SupportsHdr10.Should().Be(1);
    }

    [Fact]
    public async Task GetConfigurationAsync_FormatsRequestAndResolvesOnMatchingResponse()
    {
        byte[]? sentBytes = null;
        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x5678);

        Task<MoonshineHostConfigurationPayload> requestTask = client.GetConfigurationAsync(queryScope: 0x01);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 4);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.GetHostConfiguration);

        // Synthesize response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostConfigurationResponse,
            PayloadSize: 48,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x5678,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var expectedPayload = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 42,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 120,
            TargetBitrateKbps = 35000,
            MaxBitrateKbps = 60000,
            PreferredCodec = MoonshineVideoCodec.Av1,
            Hdr10Enabled = 1,
            AudioChannels = 2,
            AudioQualityMode = 0,
            AudioBitrateKbps = 256,
            InputPollingRateHz = 1000,
            MicPassthroughEnabled = 1,
            VirtualAudioDriverEnabled = 1
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in expectedPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        MoonshineHostConfigurationPayload result = await requestTask;
        result.ConfigVersion.Should().Be(42);
        result.DisplayWidth.Should().Be(2560);
        result.DisplayHeight.Should().Be(1440);
        result.RefreshRateHz.Should().Be(120);
        result.TargetBitrateKbps.Should().Be(35000);
        result.PreferredCodec.Should().Be(MoonshineVideoCodec.Av1);
    }

    [Fact]
    public async Task SetConfigurationAsync_FormatsRequestAndResolvesOnMatchingResponse()
    {
        byte[]? sentBytes = null;
        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x9999);

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 15000,
            MaxBitrateKbps = 30000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        Task<(MoonshineErrorCode StatusCode, uint AppliedVersion)> setTask = client.SetConfigurationAsync(proposed);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 48);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.SetHostConfiguration);

        // Synthesize response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 8];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfigurationResponse,
            PayloadSize: 8,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x9999,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var setRespPayload = new MoonshineSetHostConfigurationResponsePayload
        {
            StatusCode = MoonshineErrorCode.Success,
            AppliedConfigVersion = 2
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(in setRespPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        (MoonshineErrorCode status, uint version) = await setTask;
        status.Should().Be(MoonshineErrorCode.Success);
        version.Should().Be(2);
    }

    [Fact]
    public void ProcessIncomingControlMessage_FiresConfigurationChangedEvent()
    {
        using var client = new MoonshineRemoteHostControlClient();
        MoonshineConfigurationChangedPayload? receivedEvent = null;

        client.ConfigurationChanged += payload =>
        {
            receivedEvent = payload;
        };

        byte[] packet = new byte[MoonshineProtocolConstants.HeaderSize + 8];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.ConfigurationChanged,
            PayloadSize: 8,
            SequenceNumber: 100,
            SessionId: 1,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var payload = new MoonshineConfigurationChangedPayload
        {
            NewConfigVersion = 5,
            ChangeReasonFlags = 0x02
        };

        MoonshineProtocolCodec.TryWriteHeader(in header, packet);
        MoonshineProtocolCodec.TryWriteConfigurationChanged(in payload, packet.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(packet);

        receivedEvent.Should().NotBeNull();
        receivedEvent!.Value.NewConfigVersion.Should().Be(5);
        receivedEvent!.Value.ChangeReasonFlags.Should().Be(0x02);
    }

    [Fact]
    public async Task RequestCancellation_CancelsPendingTask()
    {
        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        Task<MoonshineHostCapabilitiesResponsePayload> requestTask = client.GetCapabilitiesAsync(ct: cts.Token);

        cts.Cancel();

        Func<Task> act = async () => await requestTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Dispose_CancelsAllPendingRequests()
    {
        var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) => ValueTask.CompletedTask);

        Task<MoonshineHostCapabilitiesResponsePayload> capTask = client.GetCapabilitiesAsync();
        Task<MoonshineHostConfigurationPayload> cfgTask = client.GetConfigurationAsync();
        Task<(MoonshineErrorCode, uint)> setTask = client.SetConfigurationAsync(default);

        client.Dispose();

        Func<Task> actCap = async () => await capTask;
        Func<Task> actCfg = async () => await cfgTask;
        Func<Task> actSet = async () => await setTask;

        await actCap.Should().ThrowAsync<OperationCanceledException>();
        await actCfg.Should().ThrowAsync<OperationCanceledException>();
        await actSet.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetConfigurationAsync_WithAuthenticator_SignsPayloadWithHmacTag()
    {
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 1);

        var authenticator = new MoonshineSessionAuthenticator(hmacKey);
        byte[]? sentBytes = null;

        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x9999,
            authenticator: authenticator);

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 15000,
            MaxBitrateKbps = 30000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        Task<(MoonshineErrorCode StatusCode, uint AppliedVersion)> setTask = client.SetConfigurationAsync(proposed);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 80);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.SetHostConfiguration);
        header.PayloadSize.Should().Be(80);

        // Verify that the HMAC tag is valid over the 80 bytes (Header + Config)
        ReadOnlySpan<byte> signedContent = sentBytes.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 48);
        ReadOnlySpan<byte> tag = sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize + 48, 32);

        bool isValid = authenticator.VerifyMessageAuthTag(signedContent, tag);
        isValid.Should().BeTrue();

        // Synthesise response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 8];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfigurationResponse,
            PayloadSize: 8,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x9999,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var setRespPayload = new MoonshineSetHostConfigurationResponsePayload
        {
            StatusCode = MoonshineErrorCode.Success,
            AppliedConfigVersion = 2
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(in setRespPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        (MoonshineErrorCode status, uint version) = await setTask;
        status.Should().Be(MoonshineErrorCode.Success);
        version.Should().Be(2);
    }

    [Fact]
    public void ProcessIncomingControlMessage_WithUnmatchedSequenceNumber_DiscardsResponseWithoutCompletingPendingRequests()
    {
        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) => ValueTask.CompletedTask,
            sessionId: 0x1111);

        Task<MoonshineHostCapabilitiesResponsePayload> capTask = client.GetCapabilitiesAsync();
        Task<MoonshineHostConfigurationPayload> cfgTask = client.GetConfigurationAsync();
        Task<(MoonshineErrorCode StatusCode, uint AppliedVersion)> setTask = client.SetConfigurationAsync(default);

        // 1. Synthesise unmatched response for HostCapabilitiesResponse with SequenceNumber = 9999
        byte[] unmatchedCapResponse = new byte[MoonshineProtocolConstants.HeaderSize + 32];
        var headerCap = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostCapabilitiesResponse,
            PayloadSize: 32,
            SequenceNumber: 9999,
            SessionId: 0x1111,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        var capPayload = new MoonshineHostCapabilitiesResponsePayload { MaxEncodeWidth = 3840 };
        MoonshineProtocolCodec.TryWriteHeader(in headerCap, unmatchedCapResponse);
        MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(in capPayload, unmatchedCapResponse.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(unmatchedCapResponse);
        capTask.IsCompleted.Should().BeFalse();
        cfgTask.IsCompleted.Should().BeFalse();
        setTask.IsCompleted.Should().BeFalse();

        // 2. Synthesise unmatched response for HostConfigurationResponse with SequenceNumber = 9999
        byte[] unmatchedCfgResponse = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var headerCfg = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostConfigurationResponse,
            PayloadSize: 48,
            SequenceNumber: 9999,
            SessionId: 0x1111,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        var cfgPayload = new MoonshineHostConfigurationPayload { DisplayWidth = 2560 };
        MoonshineProtocolCodec.TryWriteHeader(in headerCfg, unmatchedCfgResponse);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in cfgPayload, unmatchedCfgResponse.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(unmatchedCfgResponse);
        capTask.IsCompleted.Should().BeFalse();
        cfgTask.IsCompleted.Should().BeFalse();
        setTask.IsCompleted.Should().BeFalse();

        // 3. Synthesise unmatched response for SetHostConfigurationResponse with SequenceNumber = 9999
        byte[] unmatchedSetResponse = new byte[MoonshineProtocolConstants.HeaderSize + 8];
        var headerSet = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfigurationResponse,
            PayloadSize: 8,
            SequenceNumber: 9999,
            SessionId: 0x1111,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        var setRespPayload = new MoonshineSetHostConfigurationResponsePayload { StatusCode = MoonshineErrorCode.Success, AppliedConfigVersion = 10 };
        MoonshineProtocolCodec.TryWriteHeader(in headerSet, unmatchedSetResponse);
        MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(in setRespPayload, unmatchedSetResponse.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(unmatchedSetResponse);
        capTask.IsCompleted.Should().BeFalse();
        cfgTask.IsCompleted.Should().BeFalse();
        setTask.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetCapabilitiesAsync_WithAuthenticator_SignsPayloadWithHmacTag()
    {
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 1);

        var authenticator = new MoonshineSessionAuthenticator(hmacKey);
        byte[]? sentBytes = null;

        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x1234,
            authenticator: authenticator);

        Task<MoonshineHostCapabilitiesResponsePayload> requestTask = client.GetCapabilitiesAsync(queryMask: 0x0F);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 36);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.GetHostCapabilities);
        header.PayloadSize.Should().Be(36);
        header.SessionId.Should().Be(0x1234);

        MoonshineErrorCode maskErr = MoonshineProtocolCodec.TryReadGetHostCapabilities(sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize, 4), out uint parsedMask);
        maskErr.Should().Be(MoonshineErrorCode.Success);
        parsedMask.Should().Be(0x0F);

        // Verify that the HMAC tag is valid over the 36 bytes (Header + Mask)
        ReadOnlySpan<byte> signedContent = sentBytes.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4);
        ReadOnlySpan<byte> tag = sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32);

        bool isValid = authenticator.VerifyMessageAuthTag(signedContent, tag);
        isValid.Should().BeTrue();

        // Synthesise response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 32];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostCapabilitiesResponse,
            PayloadSize: 32,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x1234,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        var expectedPayload = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.Hevc,
            MaxEncodeWidth = 3840,
            MaxEncodeHeight = 2160,
            MaxEncodeFps = 144
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(in expectedPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        MoonshineHostCapabilitiesResponsePayload result = await requestTask;
        result.MaxEncodeWidth.Should().Be(3840);
        result.MaxEncodeFps.Should().Be(144);
    }

    [Fact]
    public async Task GetConfigurationAsync_WithAuthenticator_SignsPayloadWithHmacTag()
    {
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 1);

        var authenticator = new MoonshineSessionAuthenticator(hmacKey);
        byte[]? sentBytes = null;

        using var client = new MoonshineRemoteHostControlClient(
            customSender: (datagram, ct) =>
            {
                sentBytes = datagram.ToArray();
                return ValueTask.CompletedTask;
            },
            sessionId: 0x5678,
            authenticator: authenticator);

        Task<MoonshineHostConfigurationPayload> requestTask = client.GetConfigurationAsync(queryScope: 0x01);

        sentBytes.Should().NotBeNull();
        sentBytes!.Length.Should().Be(MoonshineProtocolConstants.HeaderSize + 36);

        MoonshineErrorCode headerErr = MoonshineProtocolCodec.TryReadHeader(sentBytes, out var header);
        headerErr.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.GetHostConfiguration);
        header.PayloadSize.Should().Be(36);
        header.SessionId.Should().Be(0x5678);

        MoonshineErrorCode scopeErr = MoonshineProtocolCodec.TryReadGetHostConfiguration(sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize, 4), out uint parsedScope);
        scopeErr.Should().Be(MoonshineErrorCode.Success);
        parsedScope.Should().Be(0x01);

        // Verify that the HMAC tag is valid over the 36 bytes (Header + Scope)
        ReadOnlySpan<byte> signedContent = sentBytes.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4);
        ReadOnlySpan<byte> tag = sentBytes.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32);

        bool isValid = authenticator.VerifyMessageAuthTag(signedContent, tag);
        isValid.Should().BeTrue();

        // Synthesise response from host
        byte[] responsePacket = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.HostConfigurationResponse,
            PayloadSize: 48,
            SequenceNumber: header.SequenceNumber,
            SessionId: 0x5678,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        var expectedPayload = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 42,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 120,
            TargetBitrateKbps = 35000,
            PreferredCodec = MoonshineVideoCodec.Av1
        };

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, responsePacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in expectedPayload, responsePacket.AsSpan(MoonshineProtocolConstants.HeaderSize));

        client.ProcessIncomingControlMessage(responsePacket);

        MoonshineHostConfigurationPayload result = await requestTask;
        result.ConfigVersion.Should().Be(42);
        result.DisplayWidth.Should().Be(2560);
        result.DisplayHeight.Should().Be(1440);
        result.TargetBitrateKbps.Should().Be(35000);
        result.PreferredCodec.Should().Be(MoonshineVideoCodec.Av1);
    }
}
