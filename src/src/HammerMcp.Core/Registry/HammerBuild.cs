namespace HammerMcp.Core.Registry;

/// <summary>One known Hammer build: how to recognise it and what it can do.</summary>
public sealed record HammerBuild
{
    public required string Id { get; init; }

    /// <summary>valve, hammerplusplus, slammin, strata, blackmesa, ...</summary>
    public required string Family { get; init; }

    public required string Branch { get; init; }

    public int? Build { get; init; }

    /// <summary>x86 or x64.</summary>
    public required string Arch { get; init; }

    public required BuildMatch Match { get; init; }

    public ShellInfo Shell { get; init; } = new();

    /// <summary>Menu command ids for WM_COMMAND (save, runMap, ...), per build.</summary>
    public Dictionary<string, int> Menu { get; init; } = [];

    /// <summary>Game config file relative to the game root.</summary>
    public string? GameConfig { get; init; }

    /// <summary>
    /// GoldSrc editors: the HKCU key where the editor keeps its options (Worldcraft 1.x-2.0 "Software\Worldcraft\Worldcraft",
    /// Worldcraft 2.1-3.3 "Software\Valve\Worldcraft", Hammer 3.x "Software\Valve\Valve Hammer Editor", mods their own).
    /// Its General\TextureFileCount / TextureFile0..N is the texture WAD list of Options > Textures.
    /// </summary>
    public string? SettingsKey { get; init; }

    /// <summary>vmf, rmf, map, jmf.</summary>
    public string MapFormat { get; init; } = "vmf";

    /// <summary>
    /// Support level of the inject adapter for this build (Advanced mode), null = never injected. "menu" = the MCP menu
    /// in Hammer (layers 0-1); later levels add live reading etc. One adapter DLL per bitness serves every build.
    /// </summary>
    public string? InjectAdapter { get; init; }

    /// <summary>
    /// Layer 2 of the adapter: where this build keeps the active document, the selection and the 3D camera, as
    /// "name": "0x…" (module-relative addresses and field offsets, see src/native/PROTOCOL.md). Sent to the adapter in
    /// its profile; empty = no live reading for this build.
    /// </summary>
    public Dictionary<string, string> Memory { get; init; } = [];

    /// <summary>What was verified live and when, e.g. "liveShell": "2026-11-10".</summary>
    public Dictionary<string, string> Verified { get; init; } = [];

    public string? Notes { get; init; }
}

/// <summary>Identifies the editor DLL loaded by a running Hammer.</summary>
public sealed record BuildMatch
{
    /// <summary>File name of the editor DLL: hammer_dll.dll, hammerplusplus_dll.dll, hammer.dll.</summary>
    public required string Dll { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>PE link date (yyyy-MM-dd), informational.</summary>
    public string? Linked { get; init; }
}

public sealed record ShellInfo
{
    public bool Present { get; init; }

    /// <summary>"file": session version = mapversion from the saved .vmf (try +1 on refusal).</summary>
    public string VersionRule { get; init; } = "file";

    /// <summary>map_check_version answers ok only inside a session.</summary>
    public bool CheckVersionOnlyInSession { get; init; } = true;
}
