using System.Text;

namespace HammerMcp.Core.Assets;

/// <summary>
/// File names in a Quake / GoldSrc PAK: header "PACK", int directoryOffset, int directoryLength; a directory entry is
/// char name[56], int offset, int size (64 bytes). No data is extracted.
/// </summary>
public static class PakFile
{
    public static VpkDirectory Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, path);
    }

    public static VpkDirectory Read(Stream stream, string path)
    {
        var r = new BinaryReader(stream);
        var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic != "PACK")
            throw new InvalidDataException($"not a PAK (magic '{magic}')");
        var offset = r.ReadInt32();
        var length = r.ReadInt32();
        if (offset < 12 || length < 0 || length % 64 != 0 || offset + (long)length > stream.Length)
            throw new InvalidDataException($"bad PAK directory ({length} bytes at {offset})");
        stream.Position = offset;
        var names = new List<string>(length / 64);
        for (var i = 0; i < length / 64; i++)
        {
            var raw = r.ReadBytes(56);
            r.ReadBytes(8); // offset, size
            var end = Array.IndexOf(raw, (byte)0);
            var name = Encoding.ASCII.GetString(raw, 0, end < 0 ? 56 : end);
            if (name.Length > 0)
                names.Add(name);
        }
        return new VpkDirectory(path, names);
    }
}
