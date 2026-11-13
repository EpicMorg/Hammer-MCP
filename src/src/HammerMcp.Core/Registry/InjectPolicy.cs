namespace HammerMcp.Core.Registry;

/// <summary>
/// Where the future Advanced mode may inject its adapter DLL. Only into standalone editor executables of a known build
/// that has an adapter - never into a game process: Source 2 Hammer (game/bin/win64/tools/hammer.dll) runs inside
/// cs2.exe / dota2.exe / hlvr.exe, and touching a VAC-protected (or any anti-cheat) game process can get the user's
/// account banned. Unknown builds are never injected either.
/// </summary>
public static class InjectPolicy
{
    /// <summary>Standalone map editor executables (Source 1 Hammer, Hammer++, Strata Hammer).</summary>
    private static readonly HashSet<string> EditorExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hammer.exe", "hammerplusplus.exe",
    };

    public sealed record Decision(bool Allowed, string Reason);

    public static Decision Check(string exePath, HammerBuild? build)
    {
        // Windows paths, also when tested elsewhere
        var cut = exePath.LastIndexOfAny(['\\', '/']);
        var dir = cut < 0 ? "" : exePath[..cut];
        var name = exePath[(cut + 1)..];
        if (File.Exists(Path.Combine(dir, "tools", "hammer.dll")) || build?.Family == "source2")
            return new(false, "Source 2 Hammer runs inside the game process (VAC / anti-cheat): injection is never allowed");
        if (!EditorExes.Contains(name))
            return new(false, $"{name} is not a standalone Hammer executable: injection into other (game) processes is never allowed");
        if (build is null)
            return new(false, "unknown Hammer build: injection is never allowed");
        if (string.IsNullOrEmpty(build.InjectAdapter))
            return new(false, $"no inject adapter for build {build.Id}");
        return new(true, $"adapter {build.InjectAdapter} for {build.Id}");
    }
}
