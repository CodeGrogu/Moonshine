using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Core.Media;
using Moonshine.Core.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;
using Xunit;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;

namespace Moonshine.Core.Tests;

public class MoonshineClientStreamingSessionTests
{
    [Fact]
    public async Task ClientStreamingSession_CreateAndStart_InitializesSocketsAndTransitionsToStreaming()
    {
        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            HostVideoPort = 48011,
            HostAudioPort = 48012,
            HostControlFeedbackPort = 48013,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            SessionId = 0x1234567890ABCDEFUL,
            StreamId = 1
        };

        var session = new MoonshineClientStreamingSession(config);
        session.State.Should().Be(ClientSessionState.Created);
        session.IsStreaming.Should().BeFalse();

        await session.StartAsync();
        session.State.Should().Be(ClientSessionState.Streaming);
        session.IsStreaming.Should().BeTrue();
        session.BoundLocalVideoPort.Should().BeGreaterThan(0);
        session.BoundLocalAudioPort.Should().BeGreaterThan(0);
        session.BoundLocalControlPort.Should().BeGreaterThan(0);

        await session.StopAsync();
        session.State.Should().Be(ClientSessionState.Closed);
        session.IsStreaming.Should().BeFalse();

        var act = async () => await session.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ClientStreamingSession_SendsKeyboardMouseGamepadInput_EncodesExactBinaryHeaders()
    {
        using var controlReceiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        controlReceiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int hostControlPort = ((IPEndPoint)controlReceiver.LocalEndPoint!).Port;

        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            HostControlFeedbackPort = hostControlPort,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            SessionId = 0xCAFEBABE11223344UL
        };

        await using var session = new MoonshineClientStreamingSession(config);
        await session.StartAsync();

        byte[] recvBuffer = new byte[2048];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        // 1. Keyboard Input
        bool sentKeyboard = session.SendKeyboardInput(keyCode: 0x41, scanCode: 0x1E, isDown: true, modifiers: 2);
        sentKeyboard.Should().BeTrue();

        var recvResult = await controlReceiver.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp);
        recvResult.ReceivedBytes.Should().Be(MoonshineProtocolConstants.HeaderSize + 12);

        var err = MoonshineProtocolCodec.TryReadHeader(recvBuffer.AsSpan(0, recvResult.ReceivedBytes), out var kbHeader);
        err.Should().Be(MoonshineErrorCode.Success);
        kbHeader.MessageType.Should().Be(MoonshineMessageType.InputKeyboard);
        kbHeader.SessionId.Should().Be(0xCAFEBABE11223344UL);

        var kbErr = MoonshineProtocolCodec.TryReadKeyboardInput(recvBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 12), out var kbPayload);
        kbErr.Should().Be(MoonshineErrorCode.Success);
        kbPayload.KeyCode.Should().Be(0x41);
        kbPayload.ScanCode.Should().Be(0x1E);
        kbPayload.IsDown.Should().Be(1);
        kbPayload.Modifiers.Should().Be(2);

        // 2. Mouse Input
        bool sentMouse = session.SendMouseInput(x: 500, y: -200, wheelY: 120, buttonFlags: 1, isAbsolute: true);
        sentMouse.Should().BeTrue();

        recvResult = await controlReceiver.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp);
        recvResult.ReceivedBytes.Should().Be(MoonshineProtocolConstants.HeaderSize + 20);

        err = MoonshineProtocolCodec.TryReadHeader(recvBuffer.AsSpan(0, recvResult.ReceivedBytes), out var mouseHeader);
        err.Should().Be(MoonshineErrorCode.Success);
        mouseHeader.MessageType.Should().Be(MoonshineMessageType.InputMouse);

        var mouseErr = MoonshineProtocolCodec.TryReadMouseInput(recvBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 20), out var mousePayload);
        mouseErr.Should().Be(MoonshineErrorCode.Success);
        mousePayload.X.Should().Be(500);
        mousePayload.Y.Should().Be(-200);
        mousePayload.WheelDeltaY.Should().Be(120);
        mousePayload.ButtonFlags.Should().Be(1);
        mousePayload.IsAbsolute.Should().Be(1);

        // 3. Gamepad Input
        bool sentGamepad = session.SendGamepadInput(
            gamepadIndex: 0,
            buttonMask: 0x1000,
            leftTrigger: 255,
            rightTrigger: 0,
            thumbLx: 10000,
            thumbLy: -10000,
            thumbRx: 0,
            thumbRy: 0);
        sentGamepad.Should().BeTrue();

        recvResult = await controlReceiver.ReceiveFromAsync(recvBuffer.AsMemory(), SocketFlags.None, remoteEp);
        recvResult.ReceivedBytes.Should().Be(MoonshineProtocolConstants.HeaderSize + 24);

        err = MoonshineProtocolCodec.TryReadHeader(recvBuffer.AsSpan(0, recvResult.ReceivedBytes), out var gpHeader);
        err.Should().Be(MoonshineErrorCode.Success);
        gpHeader.MessageType.Should().Be(MoonshineMessageType.InputGamepad);

        var gpErr = MoonshineProtocolCodec.TryReadGamepadInput(recvBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 24), out var gpPayload);
        gpErr.Should().Be(MoonshineErrorCode.Success);
        gpPayload.GamepadIndex.Should().Be(0);
        gpPayload.ButtonMask.Should().Be(0x1000);
        gpPayload.LeftTrigger.Should().Be(255);
        gpPayload.ThumbLx.Should().Be(10000);
        gpPayload.ThumbLy.Should().Be(-10000);

        session.Metrics.TotalInputPacketsSent.Should().Be(3);
    }

    [Fact]
    public async Task ClientStreamingSession_IngestsMoonshineVideoDatagrams_ReassemblesAndDispatchesFrames()
    {
        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            SessionId = 0x5566778899AABBCCUL,
            StreamId = 1,
            EnableFec = false
        };

        var completedFrames = new List<uint>();
        await using var session = new MoonshineClientStreamingSession(config);
        session.OnVideoFrameReassembled = frame =>
        {
            completedFrames.Add(frame.FrameIndex);
        };

        await session.StartAsync();
        int clientVideoPort = session.BoundLocalVideoPort;
        var clientVideoEp = new IPEndPoint(IPAddress.Loopback, clientVideoPort);

        using var senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Construct a 2-packet video frame for FrameIndex 1
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: config.SessionId, mtuPayloadSize: 100, fecDataShards: 0, fecParityShards: 0);
        byte[] rawFrameBytes = new byte[150];
        Array.Fill(rawFrameBytes, (byte)0xAB);

        var datagrams = new List<byte[]>();
        packetiser.PacketiseFrame(rawFrameBytes, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, dgram =>
        {
            datagrams.Add(dgram.ToArray());
        });

        datagrams.Should().HaveCount(2);

        foreach (var dgram in datagrams)
        {
            await senderSocket.SendToAsync(dgram, SocketFlags.None, clientVideoEp);
        }

        // Wait for reassembly
        for (int i = 0; i < 50 && session.Metrics.TotalVideoFramesCompleted == 0; i++)
        {
            await Task.Delay(20);
        }

        session.Metrics.TotalVideoPacketsReceived.Should().Be(2);
        session.Metrics.TotalVideoFramesCompleted.Should().Be(1);
        completedFrames.Should().Contain(1u);
    }

    [Fact]
    public async Task ClientStreamingSession_IngestsMoonshineAudioDatagrams_DecodesAndIncrementsMetrics()
    {
        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            SessionId = 0x5566778899AABBCCUL,
            StreamId = 1
        };

        await using var session = new MoonshineClientStreamingSession(config);
        await session.StartAsync();
        int clientAudioPort = session.BoundLocalAudioPort;
        var clientAudioEp = new IPEndPoint(IPAddress.Loopback, clientAudioPort);

        using var senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Construct Moonshine Audio datagram
        byte[] audioDatagram = new byte[MoonshineProtocolConstants.HeaderSize + MoonshineAudioPacketCodec.HeaderSize + 32];
        var mshnHdr = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.AudioPacket,
            PayloadSize: (uint)(MoonshineAudioPacketCodec.HeaderSize + 32),
            SequenceNumber: 1,
            SessionId: config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var audioHdr = new MoonshineAudioPacketHeader
        {
            StreamId = 1,
            SampleIndex = 0,
            SampleRate = 48000,
            FrameDurationUs = 5000,
            PayloadSize = 32,
            Channels = 2,
            Codec = MoonshineAudioCodec.Opus,
            Reserved = 0
        };

        MoonshineProtocolCodec.TryWriteHeader(in mshnHdr, audioDatagram).Should().BeTrue();
        MoonshineAudioPacketCodec.TryWriteHeader(in audioHdr, audioDatagram.AsSpan(MoonshineProtocolConstants.HeaderSize, MoonshineAudioPacketCodec.HeaderSize)).Should().BeTrue();

        await senderSocket.SendToAsync(audioDatagram, SocketFlags.None, clientAudioEp);

        // Wait for ingestion
        for (int i = 0; i < 50 && session.Metrics.TotalAudioPacketsReceived == 0; i++)
        {
            await Task.Delay(20);
        }

        session.Metrics.TotalAudioPacketsReceived.Should().Be(1);
    }
}
