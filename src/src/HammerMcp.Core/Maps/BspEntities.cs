using System.Text;

namespace HammerMcp.Core.Maps;

/// <summary>One entity of a compiled map's entity lump. Keys may repeat (every output is its own "OnX" "..." pair).</summary>
public sealed record BspEntity(int Index, IReadOnlyList<KeyValuePair<string, string>> KeyValues)
{
    public string? Get(string key) =>
        KeyValues.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    public string ClassName => Get("classname") ?? "?";
}

/// <summary>Reads the entity lump (lump 0) of a Source 1 .bsp ("VBSP", versions 19-21 and relatives).</summary>
public static class BspEntities
{
    public sealed record Result(int Version, IReadOnlyList<BspEntity> Entities);

    public static Result Read(string path)
    {
        using var fs = File.OpenRead(path);
        var r = new BinaryReader(fs);
        var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic != "VBSP")
            throw new InvalidDataException($"not a Source 1 BSP (magic '{magic}')");
        var version = r.ReadInt32();
        // lump_t: fileofs, filelen, version, fourCC (uncompressed size when LZMA'd); lump 0 = entities
        var offset = r.ReadInt32();
        var length = r.ReadInt32();
        if (offset < 0 || length < 0 || offset + (long)length > fs.Length)
            throw new InvalidDataException("bad entity lump bounds");
        fs.Position = offset;
        var data = r.ReadBytes(length);
        if (data.Length >= 4 && data[0] == 'L' && data[1] == 'Z' && data[2] == 'M' && data[3] == 'A')
            throw new NotSupportedException("the entity lump is LZMA-compressed (bspzip -repack); not supported yet");
        var end = Array.IndexOf(data, (byte)0);
        return new Result(version, Parse(Encoding.UTF8.GetString(data, 0, end < 0 ? data.Length : end)));
    }

    /// <summary>Parses "{ "key" "value" ... }" blocks; quotes inside values cannot be escaped, as in the engine.</summary>
    public static List<BspEntity> Parse(string text)
    {
        var list = new List<BspEntity>();
        List<KeyValuePair<string, string>>? current = null;
        string? key = null;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{')
            {
                current = [];
                key = null;
                i++;
            }
            else if (c == '}')
            {
                if (current is not null)
                    list.Add(new BspEntity(list.Count, current));
                current = null;
                i++;
            }
            else if (c == '"')
            {
                var close = text.IndexOf('"', i + 1);
                if (close < 0)
                    break;
                var token = text[(i + 1)..close];
                i = close + 1;
                if (current is null)
                    continue;
                if (key is null)
                    key = token;
                else
                {
                    current.Add(new(key, token));
                    key = null;
                }
            }
            else
                i++;
        }
        return list;
    }
}
