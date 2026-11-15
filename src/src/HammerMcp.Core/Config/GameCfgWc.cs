using System.Text;

namespace HammerMcp.Core.Config;

/// <summary>
/// GameCfg.wc - the binary game configurations of GoldSrc editors (Worldcraft 1.6-3.3, Hammer 3.4/3.5 and their mods),
/// next to the editor exe. Layout (little-endian, every string a zero-padded char[128]):
/// header "Game Configurations File\r\n\x1a\0" (28 bytes), float version, int configCount, then per config:
/// name, int fgdCount, int textureFormat, int mapFormat, [1.3: palette], gameExe, defaultSolid, defaultPoint, bsp, light,
/// vis, gameExeDir, mapDir, bspDir, [1.4: csg, modDir, baseGameDir], fgd[fgdCount].
/// Version 1.3 is written by Worldcraft 1.6-2.x, 1.4 by Hammer 3.x.
/// </summary>
public static class GameCfgWc
{
    private const string Magic = "Game Configurations File\r\n\x1a";
    private const int HeaderSize = 28;
    private const int Str = 128;

    public static List<HammerGameConfig> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    }

    /// <summary>Relative FGD paths ("fgd\halflife.fgd") are resolved against <paramref name="baseDir"/>, the editor's folder.</summary>
    public static List<HammerGameConfig> Parse(Stream stream, string baseDir)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var d = ms.ToArray();
        if (d.Length < HeaderSize + 8 || AnsiString(d, 0, Magic.Length) != Magic)
            throw new InvalidDataException("not a GameCfg.wc (no 'Game Configurations File' header)");
        var version = BitConverter.ToSingle(d, HeaderSize);
        var count = BitConverter.ToInt32(d, HeaderSize + 4);
        bool v13 = Math.Abs(version - 1.3f) < 0.01f, v14 = Math.Abs(version - 1.4f) < 0.01f;
        if (!v13 && !v14)
            throw new InvalidDataException($"GameCfg.wc version {version.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not supported (known: 1.3, 1.4)");
        if (count is < 0 or > 1000)
            throw new InvalidDataException($"GameCfg.wc: bad config count {count}");
        var result = new List<HammerGameConfig>();
        var pos = HeaderSize + 8;
        for (var i = 0; i < count; i++)
        {
            string Next()
            {
                Need(d, pos, Str);
                var s = AnsiString(d, pos, Str);
                pos += Str;
                return s;
            }
            var name = Next();
            Need(d, pos, 12);
            var fgdCount = BitConverter.ToInt32(d, pos);
            pos += 12; // fgdCount, textureFormat, mapFormat
            if (fgdCount is < 0 or > 1000)
                throw new InvalidDataException($"GameCfg.wc: config '{name}' has a bad FGD count {fgdCount}");
            if (v13)
                Next(); // palette (Quake)
            var gameExe = Next();
            Next(); // default solid entity
            Next(); // default point entity
            var bsp = Next();
            var light = Next();
            var vis = Next();
            var gameExeDir = Next();
            var mapDir = Next();
            var bspDir = Next();
            string csg = "", modDir = "", baseDir2 = "";
            if (v14)
            {
                csg = Next();
                modDir = Next();
                baseDir2 = Next();
            }
            var fgds = new List<string>();
            for (var f = 0; f < fgdCount; f++)
            {
                var fgd = Next();
                if (fgd.Length > 0)
                    fgds.Add(Path.IsPathRooted(fgd) ? fgd : Path.GetFullPath(Path.Combine(baseDir, fgd)));
            }
            var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in new[] { ("GameExe", gameExe), ("BSP", bsp), ("Light", light), ("Vis", vis), ("CSG", csg), ("MapDir", mapDir), ("BSPDir", bspDir) })
            {
                if (value.Length > 0)
                    tools[key] = value;
            }
            result.Add(new HammerGameConfig(name, fgds, NullIfEmpty(modDir) ?? NullIfEmpty(baseDir2), NullIfEmpty(gameExeDir),
                NullIfEmpty(mapDir), NullIfEmpty(bspDir), tools, BaseGameDir: NullIfEmpty(baseDir2), GoldSrc: true));
        }
        return result;
    }

    private static void Need(byte[] d, int pos, int length)
    {
        if (pos + length > d.Length)
            throw new InvalidDataException("GameCfg.wc is truncated");
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static readonly Encoding Ansi = CreateAnsi();

    /// <summary>Paths are in the system ANSI code page (the editors are not Unicode).</summary>
    private static Encoding CreateAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(0);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    private static string AnsiString(byte[] d, int offset, int length)
    {
        var end = Array.IndexOf(d, (byte)0, offset, length);
        return Ansi.GetString(d, offset, (end < 0 ? offset + length : end) - offset).Trim();
    }
}
