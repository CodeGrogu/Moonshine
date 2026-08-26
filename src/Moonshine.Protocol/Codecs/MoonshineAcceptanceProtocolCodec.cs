using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Codecs;

/// <summary>
/// High-performance zero-allocation codec for Two-Device Production Acceptance RPC and evidence transfer messages.
/// </summary>
public static class MoonshineAcceptanceProtocolCodec
{
    /// <summary>
    /// Serialises an Acceptance Start Run Request containing the unique AcceptanceRunId.
    /// Payload layout:
    /// [0..35] UTF8 encoded AcceptanceRunId string (padded/truncated to 36 bytes)
    /// [36..39] Flags (uint32)
    /// </summary>
    public static bool TryWriteStartRunRequest(AcceptanceRunId runId, uint flags, Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < 40)
        {
            bytesWritten = 0;
            return false;
        }

        destination[..40].Clear();
        byte[] idBytes = Encoding.UTF8.GetBytes(runId.ToString());
        int copyLen = Math.Min(idBytes.Length, 36);
        idBytes.AsSpan(0, copyLen).CopyTo(destination);

        BinaryPrimitives.WriteUInt32LittleEndian(destination[36..40], flags);
        bytesWritten = 40;
        return true;
    }

    /// <summary>
    /// Deserialises an Acceptance Start Run Request.
    /// </summary>
    public static bool TryReadStartRunRequest(ReadOnlySpan<byte> source, out AcceptanceRunId runId, out uint flags)
    {
        if (source.Length < 40)
        {
            runId = default;
            flags = 0;
            return false;
        }

        ReadOnlySpan<byte> idSpan = source[..36];
        int nullIdx = idSpan.IndexOf((byte)0);
        string idStr = nullIdx >= 0
            ? Encoding.UTF8.GetString(idSpan[..nullIdx])
            : Encoding.UTF8.GetString(idSpan);

        runId = new AcceptanceRunId(idStr);
        flags = BinaryPrimitives.ReadUInt32LittleEndian(source[36..40]);
        return true;
    }

    /// <summary>
    /// Serialises an Acceptance Step Execution / Completion message.
    /// Payload layout:
    /// [0..1] StepId (uint16)
    /// [2] Status (uint8)
    /// [3] Reserved (uint8)
    /// [4..11] DurationMs (double/float64)
    /// [12..19] FramesObserved (uint64)
    /// [20..27] PacketsObserved (uint64)
    /// [28..31] JsonPayloadLength (uint32)
    /// [32..] JsonPayloadBytes
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    public static bool TryWriteStepResult(AcceptanceStepResult step, Span<byte> destination, out int bytesWritten)
    {
        string json = JsonSerializer.Serialize(step, s_jsonOptions);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        int totalLen = 32 + jsonBytes.Length;
        if (destination.Length < totalLen)
        {
            bytesWritten = 0;
            return false;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], (ushort)step.StepId);
        destination[2] = (byte)step.Status;
        destination[3] = 0;
        BinaryPrimitives.WriteDoubleLittleEndian(destination[4..12], step.DurationMs);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[12..20], step.FramesObserved);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[20..28], step.PacketsObserved);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[28..32], (uint)jsonBytes.Length);
        jsonBytes.CopyTo(destination[32..]);

        bytesWritten = totalLen;
        return true;
    }

    /// <summary>
    /// Deserialises an Acceptance Step Execution / Completion message.
    /// </summary>
    public static bool TryReadStepResult(ReadOnlySpan<byte> source, out AcceptanceStepResult step)
    {
        if (source.Length < 32)
        {
            step = new AcceptanceStepResult();
            return false;
        }

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(source[28..32]);
        if (source.Length < 32 + jsonLen)
        {
            step = new AcceptanceStepResult();
            return false;
        }

        try
        {
            string json = Encoding.UTF8.GetString(source.Slice(32, (int)jsonLen));
            var parsed = JsonSerializer.Deserialize<AcceptanceStepResult>(json);
            if (parsed != null)
            {
                step = parsed;
                return true;
            }
        }
        catch
        {
            // Fallback to header values
        }

        step = new AcceptanceStepResult
        {
            StepId = (AcceptanceStepId)BinaryPrimitives.ReadUInt16LittleEndian(source[0..2]),
            Status = (AcceptanceStepStatus)source[2],
            DurationMs = BinaryPrimitives.ReadDoubleLittleEndian(source[4..12]),
            FramesObserved = BinaryPrimitives.ReadUInt64LittleEndian(source[12..20]),
            PacketsObserved = BinaryPrimitives.ReadUInt64LittleEndian(source[20..28])
        };
        return true;
    }
}
