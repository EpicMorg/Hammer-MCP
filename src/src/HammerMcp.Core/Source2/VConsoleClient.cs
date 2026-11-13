using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace HammerMcp.Core.Source2;

/// <summary>
/// Source 2 VConsole2 protocol (TCP, default 127.0.0.1:29000; the official developer console vconsole2.exe uses it).
/// Message = type[4] ("CMND", "PRNT", "AINF", "CHAN", "CVRB", ...) + uint16 BE version + uint32 BE length (header
/// included) + uint16 handle + payload. A command is "CMND" with the text + NUL; console output comes back as PRNT.
/// CS2 (2026) expects CMND version 212. Only connects to the game the user runs - nothing is injected.
/// </summary>
public sealed class VConsoleClient : IDisposable
{
    public const int DefaultPort = 29000;
    public const ushort CommandVersion = 212;
    private const int HeaderSize = 12;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly List<byte> _buffer = [];

    public sealed record Message(string Type, byte[] Payload)
    {
        /// <summary>Printable text of the payload (PRNT starts with a small binary header: channel, colour...).</summary>
        public string Text => ExtractText(Payload);
    }

    /// <summary>The game's process info sent on connect (AINF): exe, command line...</summary>
    public string? AppInfo { get; private set; }

    private VConsoleClient(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
    }

    public static async Task<VConsoleClient> ConnectAsync(string host = "127.0.0.1", int port = DefaultPort, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        await tcp.ConnectAsync(host, port, cts.Token);
        var client = new VConsoleClient(tcp);
        // the game greets with AINF / CHAN / ADON / CVRB (the cvar list can be large); drain it
        foreach (var m in await client.ReadAsync(TimeSpan.FromMilliseconds(1500), ct))
            if (m.Type == "AINF")
                client.AppInfo = m.Text;
        return client;
    }

    public static byte[] CommandPacket(string command)
    {
        var text = Encoding.UTF8.GetBytes(command + "\0");
        var packet = new byte[HeaderSize + text.Length];
        Encoding.ASCII.GetBytes("CMND", packet);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), CommandVersion);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(6), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), 0);
        text.CopyTo(packet, HeaderSize);
        return packet;
    }

    /// <summary>Sends a console command and returns the console text printed during <paramref name="wait"/>.</summary>
    public async Task<string> RunAsync(string command, TimeSpan wait, CancellationToken ct = default)
    {
        await _stream.WriteAsync(CommandPacket(command), ct);
        var messages = await ReadAsync(wait, ct);
        return string.Join("\n", messages.Where(m => m.Type == "PRNT").Select(m => m.Text).Where(t => t.Length > 0));
    }

    /// <summary>Messages received within <paramref name="window"/>.</summary>
    public async Task<List<Message>> ReadAsync(TimeSpan window, CancellationToken ct = default)
    {
        var result = new List<Message>();
        var end = DateTime.UtcNow + window;
        var chunk = new byte[65536];
        while (DateTime.UtcNow < end)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(end - DateTime.UtcNow);
            int n;
            try
            {
                n = await _stream.ReadAsync(chunk, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
            if (n == 0)
                break;
            _buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            result.AddRange(Parse(_buffer));
        }
        return result;
    }

    private static readonly string[] KnownTypes = ["AINF", "ADON", "CHAN", "PRNT", "CVRB", "CFGV", "PPCR", "CMND", "VFRS", "VPRF", "EFUL", "NETS"];

    /// <summary>Takes complete messages off the front of <paramref name="buffer"/>; resyncs on a known type after garbage.</summary>
    public static List<Message> Parse(List<byte> buffer)
    {
        var result = new List<Message>();
        while (buffer.Count >= HeaderSize)
        {
            var type = Encoding.ASCII.GetString(buffer.GetRange(0, 4).ToArray());
            var length = BinaryPrimitives.ReadUInt32BigEndian(buffer.GetRange(6, 4).ToArray());
            if (!KnownTypes.Contains(type) || length < HeaderSize)
            {
                // lost sync (a message longer than a 16-bit length field, or garbage): skip to the next known type
                var next = NextKnownType(buffer);
                if (next < 0)
                {
                    buffer.RemoveRange(0, buffer.Count - 3);
                    break;
                }
                buffer.RemoveRange(0, next);
                continue;
            }
            if (buffer.Count < length)
                break;
            result.Add(new Message(type, buffer.GetRange(HeaderSize, (int)length - HeaderSize).ToArray()));
            buffer.RemoveRange(0, (int)length);
        }
        return result;
    }

    private static int NextKnownType(List<byte> buffer)
    {
        var bytes = buffer.ToArray();
        var best = -1;
        foreach (var t in KnownTypes)
        {
            var at = bytes.AsSpan(1).IndexOf(Encoding.ASCII.GetBytes(t));
            if (at >= 0 && (best < 0 || at + 1 < best))
                best = at + 1;
        }
        return best;
    }

    public static string ExtractText(byte[] payload)
    {
        var runs = new List<string>();
        var start = -1;
        for (var i = 0; i <= payload.Length; i++)
        {
            var printable = i < payload.Length && (payload[i] >= 32 || payload[i] is 9 or 10 or 13);
            if (printable && start < 0)
                start = i;
            else if (!printable && start >= 0)
            {
                if (i - start >= 3)
                    runs.Add(Encoding.UTF8.GetString(payload, start, i - start).Trim());
                start = -1;
            }
        }
        return string.Join(" ", runs.Where(r => r.Length > 0));
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}
