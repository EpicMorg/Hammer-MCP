namespace HammerMcp.Server;

/// <summary>Journal of what the agent did; the app shows it on the Logs tab.</summary>
public sealed class ActivityLog
{
    public event Action<string>? Line;

    public void Write(string message) =>
        Line?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}
