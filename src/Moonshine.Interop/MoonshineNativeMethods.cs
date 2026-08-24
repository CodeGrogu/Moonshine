using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// High-performance source-generated P/Invoke bindings to Moonshine.Native.
/// </summary>
public static unsafe partial class MoonshineNativeMethods
{
    private const string LibraryName = "Moonshine.Native";

    static MoonshineNativeMethods()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            throw new PlatformNotSupportedException("Moonshine requires Windows 11 version 21H2 or later.");
        }

        NativeLibrary.SetDllImportResolver(typeof(MoonshineNativeMethods).Assembly, DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == LibraryName)
        {
            const string osLibName = "Moonshine.Native.dll";

            string[] searchDirs = [
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "build", "bin"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "build", "src", "Moonshine.Native")
            ];

            foreach (var dir in searchDirs)
            {
                string fullPath = Path.Combine(dir, osLibName);
                if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out var handle))
                {
                    return handle;
                }
            }
        }
        return IntPtr.Zero;
    }

    // ========================================================================
    // SIMD FEC APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_fec_encode_simd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int FecEncodeSimd(
        byte** dataShards,
        int dataShardsCount,
        byte** parityShards,
        int parityShardsCount,
        int shardSize
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_fec_reconstruct_simd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int FecReconstructSimd(
        byte** shards,
        int dataShardsCount,
        int parityShardsCount,
        int shardSize,
        int* erasedIndices,
        int erasedCount
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_fec_recover_simd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int FecRecoverSimd(
        byte** shards,
        int shardCount,
        int shardSize,
        int* erasedIndices,
        int erasedCount
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_vector_xor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void VectorXor(
        byte* dest,
        byte* src,
        nuint length
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_fec_get_simd_architecture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint FecGetSimdArchitecture();

    // ========================================================================
    // Lock-Free SPSC Ring Buffer APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_spsc_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SpscCreate(nuint capacity);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_spsc_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SpscDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_spsc_enqueue")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SpscEnqueue(IntPtr handle, in MoonshinePacketDesc packet);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_spsc_dequeue")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SpscDequeue(IntPtr handle, out MoonshinePacketDesc packet);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_spsc_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nuint SpscSize(IntPtr handle);

    // ========================================================================
    // Lock-Free SPSC Slot Return Queue APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_slot_return_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SlotReturnCreate(nuint capacity);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_slot_return_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SlotReturnDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_slot_return_enqueue")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SlotReturnEnqueue(IntPtr handle, int slotIndex);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_slot_return_dequeue")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SlotReturnDequeue(IntPtr handle, out int slotIndex);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_slot_return_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nuint SlotReturnSize(IntPtr handle);

    // ========================================================================
    // Sub-Millisecond Jitter Buffer APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_jitter_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr JitterCreate(nuint maxFrames);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_jitter_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void JitterDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_jitter_push_packet")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int JitterPushPacket(IntPtr handle, in MoonshinePacketDesc packet);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_jitter_pop_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int JitterPopFrame(IntPtr handle, out MoonshineFrameDesc outFrame);

    // ========================================================================
    // Hardware Video Decoder APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_query_caps")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VideoQueryCaps(out MoonshineDecoderCaps outCaps);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_create_d3d11")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr VideoCreateD3D11(IntPtr hwnd, uint width, uint height, uint codec);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_create_d3d12")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr VideoCreateD3D12(IntPtr hwnd, uint width, uint height, uint codec);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void VideoDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_submit_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VideoSubmitFrame(IntPtr handle, in MoonshineFrameDesc frame);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_get_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr VideoGetTexture(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VideoReset(IntPtr handle, uint width, uint height);

    // ========================================================================
    // Low-Latency DXGI Flip Model Swapchain APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SwapchainCreate(
        IntPtr hwnd,
        IntPtr d3d11Device,
        uint width,
        uint height,
        uint bufferCount,
        byte isHdr10
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SwapchainDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_present")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainPresent(IntPtr handle, uint syncInterval, uint flags);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_present_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainPresentTexture(IntPtr handle, IntPtr textureHandle, uint syncInterval, uint flags);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_resize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainResize(IntPtr handle, uint width, uint height);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_set_hdr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainSetHdr(IntPtr handle, byte isHdr10);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_set_hdr_metadata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainSetHdrMetadata(IntPtr handle, in MoonshineHdr10Metadata metadata);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainGetMetrics(IntPtr handle, out MoonshineSwapchainMetrics outMetrics);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_is_tearing_supported")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SwapchainIsTearingSupported(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_swapchain_get_waitable_object")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SwapchainGetWaitableObject(IntPtr handle);

    // ========================================================================
    // Audio APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_create_wasapi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr AudioCreateWasapi(uint sampleRate, ushort channels, ushort isExclusive);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_submit_pcm")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioSubmitPcm(IntPtr handle, float* pcmData, uint sampleCount);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioGetMetrics(IntPtr handle, out ulong outFramesRendered, out uint outUnderruns);

    // ========================================================================
    // WASAPI Master Loopback Audio Capture APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_capture_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr AudioCaptureCreate(uint sampleRate, uint channels, uint bufferDurationMs);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_capture_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioCaptureDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_capture_read_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioCaptureReadFloat(
        IntPtr handle,
        float* outBuffer,
        uint maxSamples,
        out uint outSamplesRead,
        out ulong outTimestampQpc
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_capture_read_pcm16")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioCaptureReadPcm16(
        IntPtr handle,
        short* outBuffer,
        uint maxSamples,
        out uint outSamplesRead,
        out ulong outTimestampQpc
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_capture_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioCaptureGetMetrics(
        IntPtr handle,
        out ulong outFramesCaptured,
        out ulong outSamplesCaptured,
        out uint outUnderruns,
        out uint outOverruns
    );

    // ========================================================================
    // WASAPI Microphone Audio Capture APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_capture_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr MicCaptureCreate(uint sampleRate, uint channels, uint bufferDurationMs);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_capture_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MicCaptureDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_capture_read_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int MicCaptureReadFloat(
        IntPtr handle,
        float* outBuffer,
        uint maxSamples,
        out uint outSamplesRead,
        out ulong outTimestampQpc
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_capture_is_active")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int MicCaptureIsActive(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_capture_recover")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int MicCaptureRecover(IntPtr handle);

    // ========================================================================
    // Low-Latency Multi-Channel Opus Audio Encoder APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr OpusEncoderCreate(
        uint sampleRate,
        uint channels,
        uint bitrate,
        uint frameDurationMs,
        uint complexity,
        int useVbr
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OpusEncoderDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_encode_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusEncoderEncodeFloat(
        IntPtr handle,
        float* pcmSamples,
        uint frameSamples,
        byte* outPayload,
        uint maxPayloadBytes,
        out uint outPayloadBytes
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_encode_pcm16")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusEncoderEncodePcm16(
        IntPtr handle,
        short* pcmSamples,
        uint frameSamples,
        byte* outPayload,
        uint maxPayloadBytes,
        out uint outPayloadBytes
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_set_bitrate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusEncoderSetBitrate(IntPtr handle, uint bitrate);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_set_complexity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusEncoderSetComplexity(IntPtr handle, uint complexity);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_encoder_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OpusEncoderGetMetrics(
        IntPtr handle,
        out ulong outFramesEncoded,
        out ulong outBytesEncoded,
        out double outAvgEncodeTimeUs,
        out uint outBitrate,
        out uint outStreamsCount
    );

    // ========================================================================
    // Low-Latency Multi-Channel Opus Audio Decoder APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr OpusDecoderCreate(uint sampleRate, uint channels);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OpusDecoderDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_decode_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusDecoderDecodeFloat(
        IntPtr handle,
        byte* opusPayload,
        uint payloadBytes,
        float* outPcmSamples,
        uint maxSamples,
        out uint outSamplesDecoded,
        int decodeFec
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_decode_pcm16")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpusDecoderDecodePcm16(
        IntPtr handle,
        byte* opusPayload,
        uint payloadBytes,
        short* outPcmSamples,
        uint maxSamples,
        out uint outSamplesDecoded,
        int decodeFec
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OpusDecoderReset(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_opus_decoder_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OpusDecoderGetMetrics(
        IntPtr handle,
        out ulong outFramesDecoded,
        out ulong outSamplesDecoded,
        out uint outDecodeErrors,
        out uint outConcealmentFrames,
        out double outAvgDecodeTimeUs,
        out uint outStreamsCount
    );

    // ========================================================================
    // Low-Latency Client-to-Host Microphone Virtual Audio Sink APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr MicSinkCreate(
        uint sampleRate,
        uint channels,
        uint targetLatencyMs,
        float gainMultiplier,
        float noiseGateThresholdDb,
        byte isMuted
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MicSinkDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_push_opus_packet")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int MicSinkPushOpusPacket(
        IntPtr handle,
        byte* opusPayload,
        uint payloadLen,
        uint timestamp,
        ushort sequenceNumber
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_pull_pcm")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int MicSinkPullPcm(
        IntPtr handle,
        float* outPcm,
        uint maxSamples,
        out uint outSamplesRead
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_set_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MicSinkSetGain(IntPtr handle, float gain);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_set_mute")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MicSinkSetMute(IntPtr handle, byte isMuted);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_mic_sink_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MicSinkGetMetrics(
        IntPtr handle,
        out ulong outPacketsReceived,
        out ulong outSamplesRendered,
        out uint outLossCount,
        out uint outDriftCorrections,
        out double outJitterMs
    );

    // ========================================================================
    // Dedicated Windows Virtual Audio Driver Controller APIs
    // ========================================================================

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VirtualAudioDriverStatusInterop
    {
        public byte IsInstalled;
        public byte IsRenderEndpointPresent;
        public byte IsCaptureEndpointPresent;
        public uint SupportedSampleRatesCount;
        public uint SupportedChannelsCount;
        public fixed byte DriverVersion[32];

        public readonly string GetDriverVersion()
        {
            fixed (byte* ptr = DriverVersion)
            {
                return Marshal.PtrToStringAnsi((IntPtr)ptr) ?? string.Empty;
            }
        }
    }

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr VirtualAudioDriverCreate();

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void VirtualAudioDriverDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_is_installed")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverIsInstalled(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_get_status")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverGetStatus(IntPtr handle, out VirtualAudioDriverStatusInterop outStatus);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_validate_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverValidateFormat(
        IntPtr handle,
        uint sampleRate,
        uint channels,
        uint formatType
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_get_endpoint_names")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverGetEndpointNames(
        IntPtr handle,
        byte* outRenderName,
        uint renderNameMaxLen,
        byte* outCaptureName,
        uint captureNameMaxLen
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_enable_mmcss")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverEnableMmcss(IntPtr handle, out IntPtr outTaskHandle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_disable_mmcss")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverDisableMmcss(IntPtr handle, IntPtr taskHandle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_get_installation_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverGetInstallationState(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_install", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverInstall(IntPtr handle, string infPath);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_remove")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverRemove(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_virtual_audio_driver_restart")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VirtualAudioDriverRestart(IntPtr handle);

    // ========================================================================
    // Real-Time Shared Memory IPC Bridge APIs
    // ========================================================================

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioIpcMetricsInterop
    {
        public uint RenderPacketsRead;
        public uint RenderUnderruns;
        public uint RenderOverruns;
        public uint CapturePacketsWritten;
        public uint CaptureUnderruns;
        public uint CaptureOverruns;
        public uint SampleRate;
        public uint Channels;
        public uint IsConnected;
    }

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr AudioIpcBridgeCreate(int isHostServer, uint sampleRate, uint channels);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioIpcBridgeDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_is_connected")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioIpcBridgeIsConnected(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_write_capture_pcm")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial long AudioIpcBridgeWriteCapturePcm(
        IntPtr handle,
        float* pcmSamples,
        uint sampleCount
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_read_render_pcm")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial long AudioIpcBridgeReadRenderPcm(
        IntPtr handle,
        float* outPcmSamples,
        uint maxSamples,
        int waitEvent,
        uint timeoutMs
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_wait_render_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioIpcBridgeWaitRenderEvent(IntPtr handle, uint timeoutMs);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_get_metrics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioIpcBridgeGetMetrics(IntPtr handle, out AudioIpcMetricsInterop outMetrics);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_enable_mmcss")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AudioIpcBridgeEnableMmcss(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_audio_ipc_bridge_revert_mmcss")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AudioIpcBridgeRevertMmcss(IntPtr handle);

    // ========================================================================
    // Zero-Copy Direct3D Desktop Capture APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_create_dxgi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr CaptureCreateDxgi(
        uint adapterIndex,
        uint outputIndex,
        out uint outWidth,
        out uint outHeight
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_create_wgc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr CaptureCreateWgc(
        IntPtr hmonitor,
        uint targetFps,
        out uint outWidth,
        out uint outHeight
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void CaptureDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_acquire_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureAcquireFrame(
        IntPtr handle,
        uint timeoutMs,
        out MoonshineCaptureFrameDesc outFrame
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_release_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void CaptureReleaseFrame(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_recover")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureRecover(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint CaptureGetFormat(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_is_hdr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureIsHdr(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_adapter_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint CaptureGetAdapterCount();

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_adapter_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureGetAdapterInfo(
        uint adapterIndex,
        out MoonshineAdapterInfo outInfo
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_display_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint CaptureGetDisplayCount(uint adapterIndex);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_display_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureGetDisplayInfo(
        uint adapterIndex,
        uint displayIndex,
        out MoonshineDisplayInfo outInfo
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_display_extended_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CaptureGetDisplayExtendedInfo(
        uint adapterIndex,
        uint displayIndex,
        out MoonshineDisplayExtendedInfo outInfo
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_display_mode_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint CaptureGetDisplayModeCount(
        uint adapterIndex,
        uint displayIndex
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_capture_get_display_modes")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial int CaptureGetDisplayModes(
        uint adapterIndex,
        uint displayIndex,
        MoonshineDisplayModeDesc* outModes,
        uint maxModes,
        out uint outModeCount
    );

    // ========================================================================
    // HDR10 Metadata Extraction & Real-Time Color Space Conversion APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_hdr_extract_metadata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int HdrExtractMetadata(
        IntPtr hmonitor,
        out MoonshineHdr10Metadata outMetadata
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_hdr_parse_capabilities")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int HdrParseCapabilities(
        uint colorSpaceDxgi,
        out MoonshineHdr10Metadata outMetadata
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_color_converter_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr ColorConverterCreate(
        IntPtr d3d11Device,
        uint width,
        uint height,
        uint inFormat,
        uint outFormat
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_color_converter_convert")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ColorConverterConvert(
        IntPtr handle,
        IntPtr inTexture,
        IntPtr outTexture
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_color_converter_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ColorConverterDestroy(IntPtr handle);

    // ========================================================================
    // Multi-Vendor Hardware Video Encoder APIs (NVENC, AMF, QuickSync, D3D11)
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_query_caps")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderQueryCaps(
        uint vendor,
        IntPtr d3dDevice,
        out MoonshineEncoderCaps outCaps
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr EncoderCreate(
        uint vendor,
        IntPtr d3dDevice,
        in MoonshineEncoderConfig config
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_encode_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderEncodeFrame(
        IntPtr handle,
        IntPtr d3dTexture,
        int forceIdr,
        out MoonshineEncodedPacketDesc outDesc,
        byte* outBuffer,
        uint maxBufferSize,
        out uint outSize
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_reconfigure")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderReconfigure(
        IntPtr handle,
        in MoonshineEncoderConfig newConfig
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_request_keyframe")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void EncoderRequestKeyframe(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void EncoderDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_get_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderGetState(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_is_healthy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderIsHealthy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_encoder_get_vendor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint EncoderGetVendor(IntPtr handle);

    // ========================================================================
    // NVIDIA NVENC Dedicated Custom APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_nvenc_query_codec_support")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int NvencQueryCodecSupport(
        uint codec,
        out uint outSupported
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_nvenc_set_tuning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int NvencSetTuning(
        IntPtr handle,
        uint preset,
        uint tuning
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_nvenc_set_intra_refresh")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int NvencSetIntraRefresh(
        IntPtr handle,
        int enable,
        uint period,
        uint count
    );

    // ========================================================================
    // Direct3D 11 Hardware Device & Texture Utility APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_d3d11_create_device")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr D3D11CreateDevice(uint vendorId);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_d3d11_destroy_device")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void D3D11DestroyDevice(IntPtr d3dDevice);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_d3d11_create_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr D3D11CreateTexture(IntPtr d3dDevice, uint width, uint height, uint format);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_d3d11_destroy_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void D3D11DestroyTexture(IntPtr texture);

    // ========================================================================
    // AMD AMF Dedicated Custom APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_amf_query_codec_support")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AmfQueryCodecSupport(
        uint codec,
        out uint outSupported
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_amf_set_tuning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AmfSetTuning(
        IntPtr handle,
        uint preset,
        uint usage
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_amf_set_intra_refresh")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AmfSetIntraRefresh(
        IntPtr handle,
        int enable,
        uint mbsPerSlot
    );

    // ========================================================================
    // Intel QuickSync / oneVPL Dedicated Custom APIs
    // ========================================================================

    [LibraryImport(LibraryName, EntryPoint = "moonshine_qsv_query_codec_support")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int QsvQueryCodecSupport(
        uint codec,
        out uint outSupported
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_qsv_set_tuning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int QsvSetTuning(
        IntPtr handle,
        uint targetUsage,
        int lowPowerVdenc
    );

    [LibraryImport(LibraryName, EntryPoint = "moonshine_qsv_set_intra_refresh")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int QsvSetIntraRefresh(
        IntPtr handle,
        int enable,
        uint cycleSize,
        int qpDelta
    );
}
