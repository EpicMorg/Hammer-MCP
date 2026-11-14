namespace HammerMcp.App;

/// <summary>
/// Appends log lines to a daily file (hammer-mcp-yyyy-MM-dd.log) in the chosen folder. A failure to write is reported
/// once (through <see cref="Failed"/>) and the file log switches itself off until it is configured again.
/// </summary>
internal sealed class LogFile
{
    private readonly object _lock = new();
    private string? _folder;

    /// <summary>Raised once when writing fails (folder missing / no access); the text says why.</summary>
    public event Action<string>? Failed;

    public string? Folder
    {
        get { lock (_lock) return _folder; }
    }

    /// <summary>Folder to write to; null switches file logging off.</summary>
    public void Configure(string? folder)
    {
        lock (_lock)
            _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    public string PathFor(DateTime day) => Path.Combine(Folder ?? "", $"hammer-mcp-{day:yyyy-MM-dd}.log");

    public void Write(string line)
    {
        string? error = null;
        lock (_lock)
        {
            if (_folder is null)
                return;
            try
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(Path.Combine(_folder, $"hammer-mcp-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error = $"log file disabled: cannot write to {_folder}: {e.Message}";
                _folder = null;
            }
        }
        if (error is not null)
            Failed?.Invoke(error);
    }
}
