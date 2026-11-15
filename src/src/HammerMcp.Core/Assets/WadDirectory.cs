using System.Text;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Texture names in a GoldSrc / Quake WAD (WAD3 / WAD2): header "WADn", int lumpCount, int directoryOffset; a directory
/// entry is int offset, int diskSize, int size, byte type, byte compression, short pad, char name[16]. Only mip textures
/// (type 0x43 WAD3, 0x44 WAD2) are kept - the names a brush face or infodecal uses.
/// </summary>
public sealed class WadDirectory
{
    public string Path { get; }
    public IReadOnlyList<string> Textures { get; }

    private WadDirectory(string path, IReadOnlyList<string> textures)
    {
        Path = path;
        Textures = textures;
    }

    public static WadDirectory Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, path);
    }

    public static WadDirectory Read(Stream stream, string path)
    {
        var r = new BinaryReader(stream);
        var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic is not ("WAD3" or "WAD2"))
            throw new InvalidDataException($"not a WAD (magic '{magic}')");
        var count = r.ReadInt32();
        var offset = r.ReadInt32();
        if (count < 0 || count > 100_000 || offset < 12 || offset + (long)count * 32 > stream.Length)
            throw new InvalidDataException($"bad WAD directory ({count} lumps at {offset})");
        stream.Position = offset;
        var names = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            r.ReadBytes(12); // offset, diskSize, size
            var type = r.ReadByte();
            r.ReadBytes(3); // compression, pad
            var raw = r.ReadBytes(16);
            var end = Array.IndexOf(raw, (byte)0);
            var name = Encoding.ASCII.GetString(raw, 0, end < 0 ? 16 : end);
            if (type is 0x43 or 0x44 && name.Length > 0 && seen.Add(name))
                names.Add(name);
        }
        return new WadDirectory(path, names);
    }
}
