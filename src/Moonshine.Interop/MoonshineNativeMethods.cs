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
        NativeLibrary.SetDllImportResolver(typeof(MoonshineNativeMethods).Assembly, DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == LibraryName)
        {
            string osLibName = OperatingSystem.IsWindows() ? "Moonshine.Native.dll" :
                               OperatingSystem.IsMacOS() ? "libMoonshine.Native.dylib" : "libMoonshine.Native.so";

            string[] searchDirs = [
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "runtimes", OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "native"),
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

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void VideoDestroy(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_video_submit_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int VideoSubmitFrame(IntPtr handle, in MoonshineFrameDesc frame);

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
}
