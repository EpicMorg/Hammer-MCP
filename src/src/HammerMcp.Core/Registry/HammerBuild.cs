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

    /// <summary>vmf, rmf, map, jmf.</summary>
    public string MapFormat { get; init; } = "vmf";

    /// <summary>Id of the C++ inject adapter for expert mode, if one exists.</summary>
    public string? InjectAdapter { get; init; }

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
