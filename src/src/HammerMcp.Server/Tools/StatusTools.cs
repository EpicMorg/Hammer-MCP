using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Pe;
using HammerMcp.Core.Registry;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class StatusTools(HammerRegistry registry, HammerTarget target, HammerMcpServerOptions options)
{
    private static readonly string[] EditorDlls = ["hammerplusplus_dll.dll", "hammer_dll.dll", "hammer.dll"];

    [McpServerTool(Name = "hammer_status", ReadOnly = true)]
    [Description("Which Hammer the server works with (process, exe, known build), which map is open, its saved mapversion, and what the agent may do (mode).")]
    public HammerStatus Status()
    {
        var process = target.Current();
        if (process is null)
            return new HammerStatus(Running: false, Mode: options.Mode.ToString(), LiveEdits: false);

        var mapPath = process.OpenMapPath;
        int? mapVersion = mapPath is not null && File.Exists(mapPath) ? VmfVersionInfo.Read(mapPath).MapVersion : null;
        var (build, dll) = Identify(registry, process.ExePath);
        return new HammerStatus(
            Running: true,
            Mode: options.Mode.ToString(),
            LiveEdits: options.AllowsLiveEdits && process.HasShellChannel,
            ProcessId: process.ProcessId,
            ExePath: process.ExePath,
            Arch: PeHeader.TryRead(process.ExePath)?.Arch,
            EditorDll: dll,
            BuildId: build?.Id,
            Family: build?.Family,
            ShellChannel: process.HasShellChannel,
            OpenMap: mapPath,
            SavedMapVersion: mapVersion);
    }

    /// <summary>Finds the editor DLL next to the exe and looks its sha256 up in the registry.</summary>
    public static (HammerBuild? Build, string? Dll) Identify(HammerRegistry registry, string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null)
            return (null, null);
        foreach (var name in EditorDlls)
        {
            // Hammer++ keeps its DLL in bin/hammerplusplus/bin next to hammerplusplus.exe
            foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "hammerplusplus", "bin", name) })
            {
                if (!File.Exists(candidate))
                    continue;
                using var stream = File.OpenRead(candidate);
                var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                return (registry.FindBySha256(hash), candidate);
            }
        }
        return (null, null);
    }
}

public sealed record HammerStatus(
    bool Running,
    string Mode,
    bool LiveEdits,
    int? ProcessId = null,
    string? ExePath = null,
    string? Arch = null,
    string? EditorDll = null,
    string? BuildId = null,
    string? Family = null,
    bool ShellChannel = false,
    string? OpenMap = null,
    int? SavedMapVersion = null);
