using System.Text.Json;
using HammerMcp.Server;

namespace HammerMcp.App;

/// <summary>User settings, stored in %APPDATA%\EpicMorg\Hammer-MCP\settings.json.</summary>
internal sealed record AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EpicMorg", "Hammer-MCP", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public McpMode Mode { get; init; } = McpMode.Auto;
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 47800;
    public string Token { get; init; } = "";

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public HammerMcpServerOptions ToServerOptions() => new()
    {
        Mode = Mode,
        BindAddress = BindAddress,
        Port = Port,
        Token = Token,
        RegistryPath = Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json"),
    };
}
