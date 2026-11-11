using System.Numerics;

namespace HammerMcp.Core.Maps;

/// <summary>
/// Point entities created / deleted through this server since the map was last saved. Together with the saved .vmf
/// it tells how many entities of a class sit at an origin - the shell channel cannot tell them apart.
/// Reset whenever the .vmf on disk changes (after a save it already contains these edits).
/// </summary>
public sealed class EditJournal
{
    private readonly Lock _lock = new();
    private string? _map;
    private DateTime _savedAt;
    private readonly List<(string Class, Vector3 Origin, int Delta)> _changes = [];

    /// <summary>Entities of a class at an origin: saved .vmf + created - deleted since the save.</summary>
    public int Count(string mapPath, IReadOnlyList<VmfEntity> saved, string className, Vector3 origin)
    {
        lock (_lock)
        {
            Sync(mapPath);
            var count = saved.At(className, origin).Count();
            foreach (var c in _changes)
            {
                if (string.Equals(c.Class, className, StringComparison.OrdinalIgnoreCase) && VmfEntities.SameOrigin(c.Origin, origin))
                    count += c.Delta;
            }
            return Math.Max(count, 0);
        }
    }

    public void Created(string mapPath, string className, Vector3 origin) => Add(mapPath, className, origin, +1);

    public void Deleted(string mapPath, string className, Vector3 origin) => Add(mapPath, className, origin, -1);

    private void Add(string mapPath, string className, Vector3 origin, int delta)
    {
        lock (_lock)
        {
            Sync(mapPath);
            _changes.Add((className, origin, delta));
        }
    }

    private void Sync(string mapPath)
    {
        var written = File.GetLastWriteTimeUtc(mapPath);
        if (_map == mapPath && _savedAt == written)
            return;
        _map = mapPath;
        _savedAt = written;
        _changes.Clear();
    }
}
