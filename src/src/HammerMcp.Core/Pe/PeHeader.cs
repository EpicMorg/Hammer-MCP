namespace HammerMcp.Core.Pe;

/// <summary>Minimal PE header reader: machine (x86/x64) and link timestamp.</summary>
public sealed record PeHeader(string Arch, DateTimeOffset Linked)
{
    public static PeHeader? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D) // MZ
                return null;
            stream.Position = 0x3C;
            stream.Position = reader.ReadInt32();
            if (reader.ReadUInt32() != 0x00004550) // PE\0\0
                return null;
            var machine = reader.ReadUInt16();
            reader.ReadUInt16(); // number of sections
            var timestamp = reader.ReadUInt32();
            var arch = machine switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0xAA64 => "arm64",
                _ => $"0x{machine:X4}",
            };
            return new PeHeader(arch, DateTimeOffset.FromUnixTimeSeconds(timestamp));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }
}
