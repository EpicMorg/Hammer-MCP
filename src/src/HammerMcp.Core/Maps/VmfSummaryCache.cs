namespace HammerMcp.Core.Maps;

/// <summary>Re-reads a .vmf summary only when the file changed (size or write time).</summary>
public sealed class VmfSummaryCache
{
    private string? _path;
    private long _size;
    private DateTime _written;
    private VmfSummary? _summary;

    public VmfSummary? Get(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return null;
        if (_summary is null || _path != path || _size != info.Length || _written != info.LastWriteTimeUtc)
        {
            _summary = VmfSummary.Read(path);
            (_path, _size, _written) = (path, info.Length, info.LastWriteTimeUtc);
        }
        return _summary;
    }
}
