using System.Runtime.Versioning;
using HammerMcp.Core.Maps;

namespace HammerMcp.Shell;

/// <summary>
/// An edit session on the map open in Hammer. Rules found live (H++ 8873 x64, 2026-11-10):
/// nested session_begin is refused, so a stale session is closed first; the accepted version is the saved
/// .vmf mapversion, occasionally +1; Ctrl+S inside a session keeps it open.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellSession : IDisposable
{
    private static readonly int[] VersionOffsets = [0, 1, -1];

    private readonly ShellChannel _channel;

    public string MapName { get; }
    public int AcceptedVersion { get; }
    public bool IsOpen { get; private set; }

    private ShellSession(ShellChannel channel, string mapName, int acceptedVersion)
    {
        _channel = channel;
        MapName = mapName;
        AcceptedVersion = acceptedVersion;
        IsOpen = true;
    }

    /// <summary>Opens a session for the saved .vmf; returns null if Hammer refuses every candidate version.</summary>
    public static ShellSession? Begin(ShellChannel channel, string vmfPath)
    {
        var mapName = Path.GetFileNameWithoutExtension(vmfPath);
        var fileVersion = VmfVersionInfo.Read(vmfPath).MapVersion;

        channel.Send(ShellCommands.SessionEnd()); // close a session left open by someone else
        foreach (var offset in VersionOffsets)
        {
            var version = fileVersion + offset;
            if (channel.Send(ShellCommands.SessionBegin(mapName, version)) == ShellResult.Ok)
                return new ShellSession(channel, mapName, version);
        }
        return null;
    }

    public ShellResult Send(string command) =>
        IsOpen ? _channel.Send(command) : throw new InvalidOperationException("session is closed");

    public void Dispose()
    {
        if (!IsOpen)
            return;
        _channel.Send(ShellCommands.SessionEnd());
        IsOpen = false;
    }
}
