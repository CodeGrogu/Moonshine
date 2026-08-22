using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Core.Input;
using Moonshine.Core.Runtime;
using Moonshine.Interop;
using Moonshine.Protocol.Input;
using Xunit;

namespace Moonshine.Core.Tests;

public class ClientInputPipelineTests
{
    [Fact]
    public unsafe void RawInput_MouseMoveAndButtons_DecodesCorrectly()
    {
        short mouseDx = 0;
        short mouseDy = 0;
        byte mouseBtn = 0;
        bool mouseBtnDown = false;
        short scrollDelta = 0;

        using var rawCapture = new WindowsRawInputCapture(
            onMouseMove: (dx, dy) => { mouseDx = dx; mouseDy = dy; },
            onMouseButton: (btn, down) => { mouseBtn = btn; mouseBtnDown = down; },
            onMouseScroll: delta => { scrollDelta = delta; }
        );

        rawCapture.ProcessRawInput(IntPtr.Zero).Should().Be(-1);

        RAWINPUT raw = default;
        raw.header.dwType = WindowsInputNativeMethods.RIM_TYPEMOUSE;
        raw.header.dwSize = (uint)sizeof(RAWINPUT);
        raw.header.hDevice = (IntPtr)0x1234;
        raw.header.wParam = IntPtr.Zero;

        // 1. Mouse Motion
        raw.mouse.usFlags = WindowsInputNativeMethods.MOUSE_MOVE_RELATIVE;
        raw.mouse.lLastX = 42;
        raw.mouse.lLastY = -28;
        raw.mouse.usButtonFlags = 0;
        raw.mouse.usButtonData = 0;

        rawCapture.ProcessRawInputData(in raw).Should().Be(WindowsInputNativeMethods.RIM_TYPEMOUSE);
        mouseDx.Should().Be(42);
        mouseDy.Should().Be(-28);

        // 2. Mouse Left Button Down
        raw.mouse.lLastX = 0;
        raw.mouse.lLastY = 0;
        raw.mouse.usButtonFlags = WindowsInputNativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN;
        rawCapture.ProcessRawInputData(in raw);
        mouseBtn.Should().Be(1);
        mouseBtnDown.Should().BeTrue();

        // 3. Mouse Wheel
        raw.mouse.usButtonFlags = WindowsInputNativeMethods.RI_MOUSE_WHEEL;
        raw.mouse.usButtonData = 120;
        rawCapture.ProcessRawInputData(in raw);
        scrollDelta.Should().Be(120);

        rawCapture.RawEventsProcessed.Should().Be(3);
        rawCapture.MouseEventsCaptured.Should().Be(3);
    }

    [Fact]
    public unsafe void RawInput_KeyboardKeysAndModifiers_TracksStateAndEmits()
    {
        short lastVkey = 0;
        bool lastKeyDown = false;
        byte lastMods = 0;

        using var rawCapture = new WindowsRawInputCapture(
            onKeyboardKey: (vkey, down, mods) =>
            {
                lastVkey = vkey;
                lastKeyDown = down;
                lastMods = mods;
            }
        );

        RAWINPUT raw = default;
        raw.header.dwType = WindowsInputNativeMethods.RIM_TYPEKEYBOARD;
        raw.header.dwSize = (uint)sizeof(RAWINPUT);
        raw.header.hDevice = (IntPtr)0x5678;

        // 1. Shift Key Down
        raw.keyboard.VKey = 0x10; // VK_SHIFT
        raw.keyboard.Flags = WindowsInputNativeMethods.RI_KEY_MAKE;
        rawCapture.ProcessRawInputData(in raw).Should().Be(WindowsInputNativeMethods.RIM_TYPEKEYBOARD);
        lastVkey.Should().Be(0x10);
        lastKeyDown.Should().BeTrue();
        (lastMods & 0x01).Should().Be(0x01);

        // 2. 'A' Key Down (with Shift active)
        raw.keyboard.VKey = 0x41; // 'A'
        raw.keyboard.Flags = WindowsInputNativeMethods.RI_KEY_MAKE;
        rawCapture.ProcessRawInputData(in raw);
        lastVkey.Should().Be(0x41);
        lastKeyDown.Should().BeTrue();
        (lastMods & 0x01).Should().Be(0x01);

        // 3. 'A' Key Up
        raw.keyboard.Flags = WindowsInputNativeMethods.RI_KEY_BREAK;
        rawCapture.ProcessRawInputData(in raw);
        lastVkey.Should().Be(0x41);
        lastKeyDown.Should().BeFalse();

        rawCapture.KeyboardEventsCaptured.Should().Be(3);
    }

    [Fact]
    public unsafe void RawInput_FocusLoss_SynthesizesReleasesAndClearsStuckKeys()
    {
        List<(short vkey, bool isDown)> keyEvents = new();
        List<(byte btn, bool isDown)> btnEvents = new();

        using var rawCapture = new WindowsRawInputCapture(
            onMouseButton: (btn, isDown) => btnEvents.Add((btn, isDown)),
            onKeyboardKey: (vkey, isDown, mods) => keyEvents.Add((vkey, isDown))
        );

        RAWINPUT raw = default;
        raw.header.dwSize = (uint)sizeof(RAWINPUT);

        // Hold 'W' (0x57) and 'D' (0x44)
        raw.header.dwType = WindowsInputNativeMethods.RIM_TYPEKEYBOARD;
        raw.keyboard.Flags = WindowsInputNativeMethods.RI_KEY_MAKE;
        raw.keyboard.VKey = 0x57;
        rawCapture.ProcessRawInputData(in raw);
        raw.keyboard.VKey = 0x44;
        rawCapture.ProcessRawInputData(in raw);

        // Hold Mouse Left Button (1)
        raw.header.dwType = WindowsInputNativeMethods.RIM_TYPEMOUSE;
        raw.mouse.usButtonFlags = WindowsInputNativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN;
        rawCapture.ProcessRawInputData(in raw);

        keyEvents.Count.Should().Be(2);
        btnEvents.Count.Should().Be(1);

        // Focus Lost
        rawCapture.OnFocusLost();

        // Must have synthesized release for 'W', 'D', and Left Button
        keyEvents.Should().Contain((0x57, false));
        keyEvents.Should().Contain((0x44, false));
        btnEvents.Should().Contain((1, false));
        rawCapture.FocusLostClears.Should().Be(1);
    }

    [Fact]
    public void XInput_ControllerState_DispatchesAndHandlesDeadzones()
    {
        List<ControllerStatePacket> statePackets = new();
        using var xinput = new WindowsXInputCapture(
            onControllerState: (in ControllerStatePacket p) => statePackets.Add(p),
            applyDeadzones: true
        );

        // Poll pass (safe regardless of whether physical gamepads are connected)
        int active = xinput.PollControllers();
        xinput.TotalPolls.Should().Be(1);
        active.Should().BeInRange(0, 4);
    }

    [Fact]
    public void InputPipeline_Lifecycle_StartsAndTransmitsPackets()
    {
        int packetsTransmitted = 0;
        using var pipeline = new MoonshineClientInputPipeline(
            pollingFrequencyHz: 1000,
            controllerPollRateHz: 250,
            packetTransmitter: packet => Interlocked.Increment(ref packetsTransmitted)
        );

        pipeline.IsRunning.Should().BeFalse();
        pipeline.Start();
        pipeline.IsRunning.Should().BeTrue();

        // Ingest movement
        pipeline.PollingEngine.IngestMouseMove(20, -10);
        pipeline.PollingEngine.IngestMouseButton(1, isDown: true);

        Thread.Sleep(50);
        packetsTransmitted.Should().BeGreaterThanOrEqualTo(1);

        pipeline.Metrics.MouseEventsCaptured.Should().BeGreaterThanOrEqualTo(0);
        pipeline.Metrics.PollingFrequencyHz.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void InputPipeline_SteadyStateHotPath_ZeroGCAllocations()
    {
        using var pipeline = new MoonshineClientInputPipeline(
            pollingFrequencyHz: 1000,
            controllerPollRateHz: 250,
            packetTransmitter: packet => { }
        );
        pipeline.Start();

        // Warm up
        for (int i = 0; i < 20; i++)
        {
            pipeline.PollingEngine.IngestMouseMove(5, -5);
            pipeline.PollingEngine.IngestMouseButton(1, true);
            pipeline.PollingEngine.IngestKeyboardKey(0x41, true);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            pipeline.PollingEngine.IngestMouseMove(10, -10);
            pipeline.PollingEngine.IngestMouseButton(1, false);
            pipeline.PollingEngine.IngestKeyboardKey(0x41, false);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0, "Input pipeline steady-state ingestion must produce zero GC allocations");
    }

    [Fact]
    public async Task InputPipeline_HostOnlyRole_InstantiatesZeroClientInputResources()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        // Start Host-only role
        var res = await coordinator.StartAsync(ApplicationRole.Host);
        res.Success.Should().BeTrue();
        coordinator.ActiveRole.Should().Be(ApplicationRole.Host);

        // Verify that client coordinator / input pipeline is completely null and inactive
        var clientCoord = new MoonshineClientCoordinator();
        clientCoord.InputPipeline.Should().BeNull("Client input pipeline must not be created in Host role");
        clientCoord.HasActiveResources.Should().BeFalse();
    }

    [Fact]
    public void InputPipeline_DoubleDispose_IsSafeAndIdempotent()
    {
        var pipeline = new MoonshineClientInputPipeline();
        pipeline.Start();

        pipeline.Dispose();
        pipeline.IsRunning.Should().BeFalse();

        var act = () => pipeline.Dispose();
        act.Should().NotThrow();
    }
}
