using System.Text;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Files inside a VPK (v1 / v2) from its *_dir.vpk tree. Data is read on demand (<see cref="ReadFile"/>): preload bytes
/// from the tree, the rest from the archive x_NNN.vpk, or from the dir file itself (archive index 0x7FFF).
/// </summary>
public sealed class VpkDirectory
{
    private const uint Signature = 0x55AA1234;
    private const ushort InDirFile = 0x7FFF;

    /// <param name="Offset">In the archive; for <see cref="InDirFile"/> from <see cref="_dataStart"/> of the dir file.</param>
    /// <param name="PreloadPos">Where the preload bytes are in the dir file.</param>
    private readonly record struct Entry(ushort Archive, uint Offset, uint Length, long PreloadPos, ushort PreloadLength);

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private long _dataStart; // end of the tree in the dir file (0 for a PAK: offsets are from the start)

    public string Path { get; }
    public IReadOnlyCollection<string> Files => _files.Keys;

    private VpkDirectory(string path) => Path = path;

    /// <summary>A pack of another format (Quake / GoldSrc .pak) with the same lookup: relative paths, any case, data at offset in the file.</summary>
    internal VpkDirectory(string path, IEnumerable<(string Name, uint Offset, uint Length)> files)
    {
        Path = path;
        foreach (var (name, offset, length) in files)
            _files.TryAdd(Normalize(name), new Entry(InDirFile, offset, length, 0, 0));
    }

    public bool Contains(string relativePath) => _files.ContainsKey(Normalize(relativePath));

    public static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    /// <summary>Opens "x.vpk" as "x_dir.vpk" (how gameinfo lists packs) or a *_dir.vpk / single-file vpk directly.</summary>
    public static VpkDirectory? Open(string path)
    {
        var dir = path.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) ? path : path[..^4] + "_dir.vpk";
        var file = File.Exists(dir) ? dir : File.Exists(path) ? path : null;
        return file is null ? null : Read(File.ReadAllBytes(file), file);
    }

    /// <summary>The bytes of a file in the pack, or null if it is not there or its archive is missing / short.</summary>
    public byte[]? ReadFile(string relativePath)
    {
        if (!_files.TryGetValue(Normalize(relativePath), out var e))
            return null;
        var data = new byte[e.PreloadLength + e.Length];
        try
        {
            if (e.PreloadLength > 0)
            {
                using var dir = File.OpenRead(Path);
                dir.Position = e.PreloadPos;
                dir.ReadExactly(data, 0, e.PreloadLength);
            }
            if (e.Length > 0)
            {
                var archive = e.Archive == InDirFile ? Path : ArchivePath(e.Archive);
                using var stream = File.OpenRead(archive);
                stream.Position = (e.Archive == InDirFile ? _dataStart : 0) + e.Offset;
                stream.ReadExactly(data, e.PreloadLength, (int)e.Length);
            }
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>x_dir.vpk → x_007.vpk.</summary>
    private string ArchivePath(ushort index) =>
        Path.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) ? $"{Path[..^"_dir.vpk".Length]}_{index:D3}.vpk" : Path;

    public static VpkDirectory Read(byte[] data, string path = "<vpk>")
    {
        if (data.Length < 12)
            throw new InvalidDataException($"{path}: truncated VPK ({data.Length} bytes)");
        using var reader = new BinaryReader(new MemoryStream(data));
        if (reader.ReadUInt32() != Signature)
            throw new InvalidDataException($"{path}: not a VPK");
        var version = reader.ReadUInt32();
        var treeSize = reader.ReadUInt32();
        if (version == 2)
            reader.BaseStream.Position += 16;
        else if (version != 1)
            throw new InvalidDataException($"{path}: unsupported VPK version {version}");

        var vpk = new VpkDirectory(path) { _dataStart = reader.BaseStream.Position + treeSize };
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
                    var archive = reader.ReadUInt16();
                    var offset = reader.ReadUInt32();
                    var length = reader.ReadUInt32();
                    reader.ReadUInt16();                 // terminator 0xFFFF
                    var preloadPos = reader.BaseStream.Position;
                    reader.BaseStream.Position += preload;
                    var file = ext == " " ? name : $"{name}.{ext}";
                    vpk._files.TryAdd(Normalize(dir == " " ? file : $"{dir}/{file}"), new Entry(archive, offset, length, preloadPos, preload));
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
