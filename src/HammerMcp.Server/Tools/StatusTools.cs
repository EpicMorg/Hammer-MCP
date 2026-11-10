using System.ComponentModel;
using System.Security.Cryptography;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Registry;
using HammerMcp.Shell;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
public sealed class StatusTools(HammerRegistry registry)
{
    private static readonly string[] EditorDlls = ["hammerplusplus_dll.dll", "hammer_dll.dll", "hammer.dll"];

    [McpServerTool(Name = "hammer_status", ReadOnly = true)]
    [Description("Which Hammer is running (process, exe, known build from the registry), which map is open and its saved mapversion.")]
    public HammerStatus Status()
    {
        var process = HammerProcess.FindShellOwner();
        if (process is null)
            return new HammerStatus(Running: false);

        var mapPath = process.OpenMapPath;
        int? mapVersion = mapPath is not null && File.Exists(mapPath) ? VmfVersionInfo.Read(mapPath).MapVersion : null;
        var build = FindBuild(process.ExePath);
        return new HammerStatus(
            Running: true,
            ProcessId: process.ProcessId,
            ExePath: process.ExePath,
            BuildId: build?.Id,
            Family: build?.Family,
            Arch: build?.Arch,
            ShellChannel: true,
            OpenMap: mapPath,
            SavedMapVersion: mapVersion);
    }

    private HammerBuild? FindBuild(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null)
            return null;
        foreach (var name in EditorDlls)
        {
            // Hammer++ keeps its DLL in bin/hammerplusplus/bin next to hammerplusplus.exe
            foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "hammerplusplus", "bin", name) })
            {
                if (!File.Exists(candidate))
                    continue;
                using var stream = File.OpenRead(candidate);
                var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                return registry.FindBySha256(hash);
            }
        }
        return null;
    }
}

public sealed record HammerStatus(
    bool Running,
    int? ProcessId = null,
    string? ExePath = null,
    string? BuildId = null,
    string? Family = null,
    string? Arch = null,
    bool ShellChannel = false,
    string? OpenMap = null,
    int? SavedMapVersion = null);
