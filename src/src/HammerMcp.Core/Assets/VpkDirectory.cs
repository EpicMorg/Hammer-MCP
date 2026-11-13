using System.Text;

namespace HammerMcp.Core.Assets;

/// <summary>File names inside a VPK (v1 / v2) read from its *_dir.vpk tree; no data is extracted.</summary>
public sealed class VpkDirectory
{
    private const uint Signature = 0x55AA1234;
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }
    public IReadOnlyCollection<string> Files => _files;

    private VpkDirectory(string path) => Path = path;

    public bool Contains(string relativePath) => _files.Contains(Normalize(relativePath));

    public static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    /// <summary>Opens "x.vpk" as "x_dir.vpk" (how gameinfo lists packs) or a *_dir.vpk / single-file vpk directly.</summary>
    public static VpkDirectory? Open(string path)
    {
        var dir = path.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) ? path : path[..^4] + "_dir.vpk";
        var file = File.Exists(dir) ? dir : File.Exists(path) ? path : null;
        return file is null ? null : Read(File.ReadAllBytes(file), file);
    }

    public static VpkDirectory Read(byte[] data, string path = "<vpk>")
    {
        if (data.Length < 12)
            throw new InvalidDataException($"{path}: truncated VPK ({data.Length} bytes)");
        using var reader = new BinaryReader(new MemoryStream(data));
        if (reader.ReadUInt32() != Signature)
            throw new InvalidDataException($"{path}: not a VPK");
        var version = reader.ReadUInt32();
        reader.ReadUInt32(); // tree size
        if (version == 2)
            reader.BaseStream.Position += 16;
        else if (version != 1)
            throw new InvalidDataException($"{path}: unsupported VPK version {version}");

        var vpk = new VpkDirectory(path);
        while (true)
        {
            var ext = ReadString(reader);
            if (ext.Length == 0)
                break;
            while (true)
            {
                var dir = ReadString(reader);
                if (dir.Length == 0)
                    break;
                while (true)
                {
                    var name = ReadString(reader);
                    if (name.Length == 0)
                        break;
                    reader.ReadUInt32();                 // CRC
                    var preload = reader.ReadUInt16();
                    reader.BaseStream.Position += 2 + 4 + 4 + 2; // archive index, offset, length, terminator
                    reader.BaseStream.Position += preload;
                    var file = ext == " " ? name : $"{name}.{ext}";
                    vpk._files.Add(Normalize(dir == " " ? file : $"{dir}/{file}"));
                }
            }
        }
        return vpk;
    }

    private static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
