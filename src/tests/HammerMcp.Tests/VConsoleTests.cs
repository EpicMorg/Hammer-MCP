using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HammerMcp.Core.Source2;

namespace HammerMcp.Tests;

public class VConsoleTests
{
    private static byte[] Msg(string type, byte[] payload)
    {
        var m = new byte[12 + payload.Length];
        Encoding.ASCII.GetBytes(type, m);
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(4), 212);
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(6), (uint)m.Length);
        payload.CopyTo(m, 12);
        return m;
    }

    private static byte[] Prnt(string text) => [.. new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 }, .. Encoding.UTF8.GetBytes(text + "\0")];

    [Fact]
    public void CommandPacketMatchesTheProtocol()
    {
        var p = VConsoleClient.CommandPacket("status");
        Assert.Equal("CMND", Encoding.ASCII.GetString(p, 0, 4));
        Assert.Equal(212, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(4)));
        Assert.Equal((uint)p.Length, BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(6)));
        Assert.Equal("status\0", Encoding.UTF8.GetString(p, 12, p.Length - 12));
    }

    [Fact]
    public void ParsesSplitAndGarbledStreams()
    {
        var all = new List<byte>();
        all.AddRange([1, 2, 3]); // garbage before the first message
        all.AddRange(Msg("PRNT", Prnt("hello")));
        var second = Msg("PRNT", Prnt("world"));
        all.AddRange(second.Take(5));
        var got = VConsoleClient.Parse(all);
        Assert.Equal("hello", Assert.Single(got).Text);
        all.AddRange(second.Skip(5));
        Assert.Equal("world", Assert.Single(VConsoleClient.Parse(all)).Text);
    }

    [Fact]
    public async Task RunsACommandAgainstAFakeGame()
    {
        var ct = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var game = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(ct);
            var s = c.GetStream();
            await s.WriteAsync(Msg("AINF", Encoding.UTF8.GetBytes("cs2.exe -tools\0")), ct);
            var buf = new byte[256];
            var n = await s.ReadAsync(buf, ct);
            var cmd = Encoding.UTF8.GetString(buf, 12, n - 13);
            await s.WriteAsync(Msg("PRNT", Prnt($"ran {cmd}")), ct);
            await Task.Delay(500, ct);
        }, ct);
        using var client = await VConsoleClient.ConnectAsync("127.0.0.1", port, ct: ct);
        Assert.Contains("cs2.exe -tools", client.AppInfo);
        Assert.Contains("ran hammer_map_files", await client.RunAsync("hammer_map_files", TimeSpan.FromMilliseconds(400), ct));
        await game;
        listener.Stop();
    }

    [Fact]
    public void ParsesHammerMapFiles()
    {
        const string output = "----\n1 : b:/steam/content/csgo_addons/x/maps/cs_script_demo.vmap\n----\n11 map files loaded\nFailed to load image data from https://a/b.webm";
        Assert.Equal([@"b:\steam\content\csgo_addons\x\maps\cs_script_demo.vmap"], VConsoleClient.MapFiles(output));
    }
}
