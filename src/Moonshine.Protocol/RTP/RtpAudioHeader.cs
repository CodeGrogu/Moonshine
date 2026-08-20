using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.RTP;

/// <summary>
/// Audio RTP Header structure used in Opus/PCM streaming over GameStream.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RtpAudioHeader
{
    public readonly RtpHeader BaseHeader;
    public readonly ushort AudioSequenceNumberRaw;
    public readonly ushort StreamIdRaw;

    public ushort AudioSequenceNumber => BinaryPrimitives.ReverseEndianness(AudioSequenceNumberRaw);
    public ushort StreamId => BinaryPrimitives.ReverseEndianness(StreamIdRaw);

    public const int Size = RtpHeader.Size + 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out RtpAudioHeader header, out ReadOnlySpan<byte> audioPayload)
    {
        if (source.Length < Size)
        {
            header = default;
            audioPayload = default;
            return false;
        }

        header = MemoryMarshal.Read<RtpAudioHeader>(source);
        audioPayload = source[Size..];
        return true;
    }
}
