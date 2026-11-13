namespace HammerMcp.Core.Source2;

/// <summary>
/// Maps open in Source 2 Hammer, asked through the game's developer console ("hammer_map_files"), cached for a few
/// seconds: the Hammer window title is not reliable (its window is not always found / shows only a file name).
/// </summary>
public static class VConsoleMaps
{
    private static readonly Lock Gate = new();
    private static (List<string> Maps, DateTime At)? _cache;
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(5);

    /// <summary>Open maps (Windows paths), empty if the console is not reachable.</summary>
    public static List<string> OpenMaps()
    {
        lock (Gate)
        {
            if (_cache is { } c && DateTime.UtcNow - c.At < CacheTime)
                return c.Maps;
        }
        List<string> maps;
        try
        {
            maps = Task.Run(async () =>
            {
                using var client = await VConsoleClient.ConnectAsync(timeout: TimeSpan.FromSeconds(2));
                return VConsoleClient.MapFiles(await client.RunAsync("hammer_map_files", TimeSpan.FromSeconds(1.5)));
            }).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or OperationCanceledException or IOException)
        {
            maps = [];
        }
        lock (Gate)
            _cache = (maps, DateTime.UtcNow);
        return maps;
    }

    /// <summary>Runs hammer_force_autosave and waits for it (Hammer writes the autosave in well under a second); false if the console is not reachable.</summary>
    public static bool ForceAutosave()
    {
        try
        {
            return Task.Run(async () =>
            {
                using var client = await VConsoleClient.ConnectAsync(timeout: TimeSpan.FromSeconds(2));
                var output = await client.RunAsync("hammer_force_autosave", TimeSpan.FromSeconds(2));
                _ = output;
                return true;
            }).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or OperationCanceledException or IOException)
        {
            return false;
        }
    }
}
