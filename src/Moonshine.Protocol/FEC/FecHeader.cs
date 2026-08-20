using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.FEC;

/// <summary>
/// Forward Error Correction (FEC) Block Header for Reed-Solomon packet recovery.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct FecHeader
{
    public readonly uint BlockIndexRaw;
    public readonly ushort ShardIndexRaw;
    public readonly ushort DataShardsRaw;
    public readonly ushort ParityShardsRaw;
    public readonly ushort ShardSizeRaw;

    public uint BlockIndex => BinaryPrimitives.ReverseEndianness(BlockIndexRaw);
    public ushort ShardIndex => BinaryPrimitives.ReverseEndianness(ShardIndexRaw);
    public ushort DataShards => BinaryPrimitives.ReverseEndianness(DataShardsRaw);
    public ushort ParityShards => BinaryPrimitives.ReverseEndianness(ParityShardsRaw);
    public ushort ShardSize => BinaryPrimitives.ReverseEndianness(ShardSizeRaw);

    public int TotalShards => DataShards + ParityShards;
    public bool IsParityShard => ShardIndex >= DataShards;

    public const int Size = 12;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out FecHeader header, out ReadOnlySpan<byte> shardPayload)
    {
        if (source.Length < Size)
        {
            header = default;
            shardPayload = default;
            return false;
        }

        header = MemoryMarshal.Read<FecHeader>(source);
        shardPayload = source[Size..];
        return true;
    }
}
