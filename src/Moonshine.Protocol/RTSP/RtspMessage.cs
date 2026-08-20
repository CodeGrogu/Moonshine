using System.Buffers;
using System.Text;

namespace Moonshine.Protocol.RTSP;

public enum RtspMethod
{
    Options,
    Describe,
    Setup,
    Play,
    Teardown,
    Announce,
    SetParameter,
    GetParameter
}

/// <summary>
/// High-performance RTSP message builder and parser with zero unnecessary allocations.
/// </summary>
public sealed class RtspMessage
{
    public RtspMethod Method { get; set; }
    public string Uri { get; set; } = string.Empty;
    public int CSeq { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Body { get; set; }

    public int StatusCode { get; set; }
    public string StatusMessage { get; set; } = "OK";
    public bool IsResponse { get; set; }

    public static RtspMessage CreateRequest(RtspMethod method, string uri, int cseq)
    {
        return new RtspMessage
        {
            IsResponse = false,
            Method = method,
            Uri = uri,
            CSeq = cseq
        };
    }

    public void Serialize(IBufferWriter<byte> writer)
    {
        var sb = new StringBuilder(256);

        if (IsResponse)
        {
            sb.Append($"RTSP/1.0 {StatusCode} {StatusMessage}\r\n");
        }
        else
        {
            sb.Append($"{Method.ToString().ToUpperInvariant()} {Uri} RTSP/1.0\r\n");
        }

        sb.Append($"CSeq: {CSeq}\r\n");

        if (!string.IsNullOrEmpty(SessionId))
        {
            sb.Append($"Session: {SessionId}\r\n");
        }

        foreach (var (key, value) in Headers)
        {
            sb.Append($"{key}: {value}\r\n");
        }

        if (!string.IsNullOrEmpty(Body))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(Body);
            sb.Append($"Content-Length: {bodyBytes.Length}\r\n\r\n");
            sb.Append(Body);
        }
        else
        {
            sb.Append("\r\n");
        }

        byte[] totalBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var span = writer.GetSpan(totalBytes.Length);
        totalBytes.CopyTo(span);
        writer.Advance(totalBytes.Length);
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out RtspMessage message)
    {
        message = new RtspMessage();
        string text = Encoding.UTF8.GetString(data);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return false;
        }

        string firstLine = lines[0];
        if (firstLine.StartsWith("RTSP/1.0", StringComparison.OrdinalIgnoreCase))
        {
            message.IsResponse = true;
            var parts = firstLine.Split(' ', 3);
            if (parts.Length >= 2 && int.TryParse(parts[1], out int status))
            {
                message.StatusCode = status;
                message.StatusMessage = parts.Length > 2 ? parts[2] : "OK";
            }
        }
        else
        {
            message.IsResponse = false;
            var parts = firstLine.Split(' ', 3);
            if (parts.Length >= 2)
            {
                message.Uri = parts[1];
                if (Enum.TryParse<RtspMethod>(parts[0], true, out var m))
                {
                    message.Method = m;
                }
            }
        }

        int bodyStartIndex = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                bodyStartIndex = i + 1;
                break;
            }

            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                string headerName = line[..colon].Trim();
                string headerValue = line[(colon + 1)..].Trim();

                if (headerName.Equals("CSeq", StringComparison.OrdinalIgnoreCase) && int.TryParse(headerValue, out int cseq))
                {
                    message.CSeq = cseq;
                }
                else if (headerName.Equals("Session", StringComparison.OrdinalIgnoreCase))
                {
                    message.SessionId = headerValue;
                }
                else
                {
                    message.Headers[headerName] = headerValue;
                }
            }
        }

        if (bodyStartIndex >= 0 && bodyStartIndex < lines.Length)
        {
            message.Body = string.Join("\r\n", lines[bodyStartIndex..]);
        }

        return true;
    }
}
