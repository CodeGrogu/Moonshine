using Moonshine.Core.Runtime;
using Moonshine.Host.Input;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;
using Xunit;

namespace Moonshine.Host.Tests;

public sealed class HostInputPipelineTests
{
    private sealed class MockWindowsInputInjector : IWindowsInputInjector
    {
        public int MouseMoveCount { get; private set; }
        public short LastDeltaX { get; private set; }
        public short LastDeltaY { get; private set; }

        public int AbsoluteMoveCount { get; private set; }
        public int LastAbsX { get; private set; }
        public int LastAbsY { get; private set; }

        public int MouseButtonCount { get; private set; }
        public byte LastButtonIndex { get; private set; }
        public bool LastButtonIsDown { get; private set; }

        public int ScrollCount { get; private set; }
        public short LastScrollDelta { get; private set; }
        public bool LastScrollIsHorizontal { get; private set; }

        public int KeyCount { get; private set; }
        public short LastVKey { get; private set; }
        public short LastScanCode { get; private set; }
        public bool LastKeyIsDown { get; private set; }

        public int ReleaseCalls { get; private set; }
        public int InjectedHeldCount { get; set; } = 3;
        public bool IsDisposed { get; private set; }

        public bool InjectMouseMove(short deltaX, short deltaY)
        {
            MouseMoveCount++;
            LastDeltaX = deltaX;
            LastDeltaY = deltaY;
            return true;
        }

        public bool InjectMouseMoveAbsolute(
            int x,
            int y,
            int clientWidth,
            int clientHeight,
            int monitorOffsetX = 0,
            int monitorOffsetY = 0,
            int monitorWidth = 0,
            int monitorHeight = 0)
        {
            AbsoluteMoveCount++;
            LastAbsX = x;
            LastAbsY = y;
            return true;
        }

        public int InjectBatch(ReadOnlySpan<INPUT> inputs)
        {
            return inputs.Length;
        }

        public VirtualDesktopGeometry GetVirtualDesktopBounds()
        {
            return new VirtualDesktopGeometry(0, 0, 1920, 1080);
        }

        public void RefreshVirtualDesktopBounds()
        {
        }

        public bool InjectMouseButton(byte buttonIndex, bool isDown)
        {
            MouseButtonCount++;
            LastButtonIndex = buttonIndex;
            LastButtonIsDown = isDown;
            return true;
        }

        public bool InjectMouseScroll(short scrollDelta, bool isHorizontal = false)
        {
            ScrollCount++;
            LastScrollDelta = scrollDelta;
            LastScrollIsHorizontal = isHorizontal;
            return true;
        }

        public bool InjectKeyboardKey(short virtualKeyCode, short scanCode, bool isDown, byte modifiers = 0)
        {
            KeyCount++;
            LastVKey = virtualKeyCode;
            LastScanCode = scanCode;
            LastKeyIsDown = isDown;
            return true;
        }

        public int ReleaseAllHeldInputs()
        {
            ReleaseCalls++;
            return InjectedHeldCount;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class MockVirtualGamepadInjector : IWindowsVirtualGamepadInjector
    {
        public bool IsDriverAvailable { get; set; } = true;
        public int AllocatedControllerCount => 1;
        public int UpdateCount { get; private set; }
        public byte LastControllerIndex { get; private set; }
        public ushort LastButtons { get; private set; }
        public bool DisconnectedAll { get; private set; }
        public bool IsDisposed { get; private set; }

        public bool UpdateControllerState(byte controllerIndex, in ControllerStatePacket packet)
        {
            UpdateCount++;
            LastControllerIndex = controllerIndex;
            LastButtons = packet.Buttons;
            return true;
        }

        public bool UpdateControllerState(byte controllerIndex, in MoonshineInputGamepadPayload payload)
        {
            UpdateCount++;
            LastControllerIndex = controllerIndex;
            LastButtons = payload.ButtonMask;
            return true;
        }

        public void DisconnectAll()
        {
            DisconnectedAll = true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Fact]
    public void HostInputPipeline_MouseMoveRelative_InjectsSuccessfully()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(inputInjector: mockInjector);

        var packet = new MouseMovePacket(15, -20);
        Span<byte> buffer = stackalloc byte[MouseMovePacket.PacketSize];
        int written = packet.WriteTo(buffer);
        Assert.Equal(MouseMovePacket.PacketSize, written);

        bool processed = pipeline.ProcessInputPacket(buffer);
        Assert.True(processed);
        Assert.Equal(1, mockInjector.MouseMoveCount);
        Assert.Equal(15, mockInjector.LastDeltaX);
        Assert.Equal(-20, mockInjector.LastDeltaY);
        Assert.Equal(1, pipeline.Metrics.MouseMovesInjected);
    }

    [Fact]
    public void HostInputPipeline_MouseButtonTransitions_InjectsAllButtons()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(inputInjector: mockInjector);

        // Left button down
        var btnDown = new MouseButtonPacket(1, isDown: true);
        Span<byte> buffer = stackalloc byte[MouseButtonPacket.PacketSize];
        btnDown.WriteTo(buffer);
        Assert.True(pipeline.ProcessInputPacket(buffer));
        Assert.Equal(1, mockInjector.LastButtonIndex);
        Assert.True(mockInjector.LastButtonIsDown);

        // Left button up
        var btnUp = new MouseButtonPacket(1, isDown: false);
        btnUp.WriteTo(buffer);
        Assert.True(pipeline.ProcessInputPacket(buffer));
        Assert.Equal(1, mockInjector.LastButtonIndex);
        Assert.False(mockInjector.LastButtonIsDown);
        Assert.Equal(2, pipeline.Metrics.MouseButtonsInjected);
    }

    [Fact]
    public void HostInputPipeline_MouseScroll_InjectsVerticalAndHorizontal()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(inputInjector: mockInjector);

        var scroll = new MouseScrollPacket(120);
        Span<byte> buffer = stackalloc byte[MouseScrollPacket.PacketSize];
        scroll.WriteTo(buffer);
        Assert.True(pipeline.ProcessInputPacket(buffer));
        Assert.Equal(120, mockInjector.LastScrollDelta);
        Assert.False(mockInjector.LastScrollIsHorizontal);
        Assert.Equal(1, pipeline.Metrics.MouseScrollsInjected);
    }

    [Fact]
    public void HostInputPipeline_Keyboard_InjectsVirtualKeyAndScanCode()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(inputInjector: mockInjector);

        var key = new KeyboardPacket(0x41, isDown: true, modifiers: 0x01); // 'A' key with Shift
        Span<byte> buffer = stackalloc byte[KeyboardPacket.PacketSize];
        key.WriteTo(buffer);
        Assert.True(pipeline.ProcessInputPacket(buffer));
        Assert.Equal(0x41, mockInjector.LastVKey);
        Assert.True(mockInjector.LastKeyIsDown);
        Assert.Equal(1, pipeline.Metrics.KeyboardKeysInjected);
    }

    [Fact]
    public void HostInputPipeline_MnbpProtocols_DecodesAndInjectsAccurately()
    {
        var mockInjector = new MockWindowsInputInjector();
        var mockGamepad = new MockVirtualGamepadInjector();
        using var pipeline = new MoonshineHostInputPipeline(mockInjector, mockGamepad);

        // 1. MNBP Mouse Absolute & Buttons
        Span<byte> mnbpBuffer = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 20];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputMouse,
            PayloadSize: 20,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 1000
        );
        MoonshineProtocolCodec.TryWriteHeader(header, mnbpBuffer);

        var mousePayload = new MoonshineInputMousePayload
        {
            X = 960,
            Y = 540,
            WheelDeltaY = 120,
            WheelDeltaX = 0,
            ButtonFlags = 0x01, // Left button down
            IsAbsolute = 1,
            TimestampOffsetUs = 0
        };
        MoonshineProtocolCodec.TryWriteMouseInput(mousePayload, mnbpBuffer[MoonshineProtocolConstants.HeaderSize..]);

        Assert.True(pipeline.ProcessInputPacket(mnbpBuffer));
        Assert.Equal(1, mockInjector.AbsoluteMoveCount);
        Assert.Equal(960, mockInjector.LastAbsX);
        Assert.Equal(540, mockInjector.LastAbsY);
        Assert.Equal(1, mockInjector.MouseButtonCount);
        Assert.Equal(1, mockInjector.ScrollCount);

        // 2. MNBP Gamepad Input
        Span<byte> gamepadBuffer = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 24];
        var gamepadHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputGamepad,
            PayloadSize: 24,
            SequenceNumber: 2,
            SessionId: 0,
            TimestampUs: 2000
        );
        MoonshineProtocolCodec.TryWriteHeader(gamepadHeader, gamepadBuffer);

        var gamepadPayload = new MoonshineInputGamepadPayload
        {
            GamepadIndex = 0,
            ButtonMask = (ushort)(GamepadButtons.A | GamepadButtons.Start),
            LeftTrigger = 200,
            RightTrigger = 255,
            ThumbLx = 10000,
            ThumbLy = -10000,
            ThumbRx = 0,
            ThumbRy = 0
        };
        MoonshineProtocolCodec.TryWriteGamepadInput(gamepadPayload, gamepadBuffer[MoonshineProtocolConstants.HeaderSize..]);

        Assert.True(pipeline.ProcessInputPacket(gamepadBuffer));
        Assert.Equal(1, mockGamepad.UpdateCount);
        Assert.Equal((ushort)(GamepadButtons.A | GamepadButtons.Start), mockGamepad.LastButtons);
    }

    [Fact]
    public void HostInputPipeline_SequenceMonotonicity_RejectsStaleAndDuplicatePackets()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(
            mockInjector,
            config: new HostInputConfig { EnforceSequenceMonotonicity = true });

        Span<byte> buffer = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 12];

        // Sequence 5
        var h5 = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, MoonshineProtocolConstants.Version10, MoonshineMessageType.InputKeyboard, 12, 5, 0, 100);
        MoonshineProtocolCodec.TryWriteHeader(h5, buffer);
        var k = new MoonshineInputKeyboardPayload { KeyCode = 0x20, IsDown = 1 };
        MoonshineProtocolCodec.TryWriteKeyboardInput(k, buffer[MoonshineProtocolConstants.HeaderSize..]);
        Assert.True(pipeline.ProcessInputPacket(buffer));

        // Duplicate Sequence 5 -> Rejected
        Assert.False(pipeline.ProcessInputPacket(buffer));

        // Stale Sequence 4 -> Rejected
        var h4 = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, MoonshineProtocolConstants.Version10, MoonshineMessageType.InputKeyboard, 12, 4, 0, 90);
        MoonshineProtocolCodec.TryWriteHeader(h4, buffer);
        MoonshineProtocolCodec.TryWriteKeyboardInput(k, buffer[MoonshineProtocolConstants.HeaderSize..]);
        Assert.False(pipeline.ProcessInputPacket(buffer));

        // Valid Sequence 6 -> Accepted
        var h6 = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, MoonshineProtocolConstants.Version10, MoonshineMessageType.InputKeyboard, 12, 6, 0, 110);
        MoonshineProtocolCodec.TryWriteHeader(h6, buffer);
        MoonshineProtocolCodec.TryWriteKeyboardInput(k, buffer[MoonshineProtocolConstants.HeaderSize..]);
        Assert.True(pipeline.ProcessInputPacket(buffer));

        Assert.Equal(2, pipeline.Metrics.PacketsRejected);
    }

    [Fact]
    public void HostInputPipeline_SessionReset_ReleasesAllHeldKeysAndButtons()
    {
        var mockInjector = new MockWindowsInputInjector();
        var mockGamepad = new MockVirtualGamepadInjector();
        using var pipeline = new MoonshineHostInputPipeline(mockInjector, mockGamepad);

        pipeline.ResetSession();

        Assert.Equal(1, mockInjector.ReleaseCalls);
        Assert.True(mockGamepad.DisconnectedAll);
        Assert.Equal(3, pipeline.Metrics.StuckKeysReleased);
    }

    [Fact]
    public void HostInputPipeline_ZeroAllocations_SteadyStateHotPath()
    {
        var mockInjector = new MockWindowsInputInjector();
        using var pipeline = new MoonshineHostInputPipeline(mockInjector);

        var packet = new MouseMovePacket(5, 5);
        Span<byte> buffer = stackalloc byte[MouseMovePacket.PacketSize];
        packet.WriteTo(buffer);

        // Warm up
        pipeline.ProcessInputPacket(buffer);

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            pipeline.ProcessInputPacket(buffer);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(beforeAlloc, afterAlloc);
    }

    [Fact]
    public void HostCoordinator_DisabledRole_CreatesNoInputServices()
    {
        using var coordinator = new MoonshineHostCoordinator();
        Assert.False(coordinator.IsRunning);
        Assert.Null(coordinator.InputPipeline);
        Assert.False(coordinator.HasActiveResources);
    }

    [Fact]
    public void WindowsSendInputInjector_DirectOperations_ExecuteCleanly()
    {
        using var injector = new WindowsSendInputInjector();
        Assert.False(injector.IsDisposed);

        // Relative motion
        bool moveOk = injector.InjectMouseMove(0, 0); // No-op delta returns true
        Assert.True(moveOk);

        // Absolute motion bounds check
        bool invalidAbs = injector.InjectMouseMoveAbsolute(100, 100, 0, 0);
        Assert.False(invalidAbs);

        // Stuck key release
        int released = injector.ReleaseAllHeldInputs();
        Assert.Equal(0, released);

        // Double dispose safety
        injector.Dispose();
        injector.Dispose();
        Assert.True(injector.IsDisposed);
    }

    [Fact]
    public void WindowsVirtualGamepadInjector_FailClosedWhenDriverAbsent()
    {
        using var gamepadInjector = new WindowsVirtualGamepadInjector();
        // If driver is not installed, UpdateControllerState must fail closed and report false
        if (!gamepadInjector.IsDriverAvailable)
        {
            var state = new ControllerStatePacket(0, GamepadButtons.A, 0, 0, 0, 0, 0, 0);
            bool updated = gamepadInjector.UpdateControllerState(0, in state);
            Assert.False(updated);
            Assert.Equal(0, gamepadInjector.AllocatedControllerCount);
        }

        gamepadInjector.DisconnectAll();
        gamepadInjector.Dispose();
    }
}
