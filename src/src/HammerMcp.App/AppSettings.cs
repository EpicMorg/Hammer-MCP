using System.Text.Json;
using System.Text.Json.Serialization;
using HammerMcp.Server;

namespace HammerMcp.App;

/// <summary>User settings, stored in %APPDATA%\EpicMorg\Hammer-MCP\settings.json (secrets encrypted, see <see cref="SettingsSecret"/>).</summary>
internal sealed record AppSettings
{
    /// <summary>The settings file; HAMMER_MCP_SETTINGS points elsewhere (tests, a second instance).</summary>
    private static readonly string FilePath = Environment.GetEnvironmentVariable("HAMMER_MCP_SETTINGS") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EpicMorg", "Hammer-MCP", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public McpMode Mode { get; init; } = McpMode.Auto;
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 47800;
    /// <summary>The server token; in the file only encrypted (<see cref="TokenProtected"/>).</summary>
    [JsonIgnore]
    public string Token { get; init; } = "";

    [JsonPropertyName("TokenProtected")]
    public string TokenProtected
    {
        get => SettingsSecret.Protect(Token);
        init => Token = SettingsSecret.Unprotect(value);
    }

    /// <summary>Settings written before 0.8.1 kept the token in plain text: read it once, never write it.</summary>
    [JsonPropertyName("Token"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlainToken
    {
        get => null;
        init
        {
            if (!string.IsNullOrEmpty(value) && Token.Length == 0)
            {
                Token = value;
                HadPlainSecrets = true;
            }
        }
    }

    /// <summary>Approval mode ("Ask me before agent edits" on the Main tab); saved as soon as it is toggled.</summary>
    public bool AskBeforeEdits { get; init; }

    /// <summary>Also write the Logs tab to daily files (hammer-mcp-yyyy-MM-dd.log).</summary>
    public bool SaveLogs { get; init; } = true;

    /// <summary>Folder for log files; empty = <see cref="DefaultLogFolder"/>.</summary>
    public string LogFolder { get; init; } = "";

    public static string DefaultLogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EpicMorg", "Hammer-MCP", "logs");

    /// <summary>OpenCode bridge (OpenCode tab): Hammer chat lines go into an OpenCode session; saved by its own Save button.</summary>
    public bool OpenCodeEnabled { get; init; }
    public string OpenCodeUrl { get; init; } = "http://127.0.0.1:4096";
    public string OpenCodeUsername { get; init; } = "opencode";
    [JsonIgnore]
    public string OpenCodePassword { get; init; } = "";

    [JsonPropertyName("OpenCodePasswordProtected")]
    public string OpenCodePasswordProtected
    {
        get => SettingsSecret.Protect(OpenCodePassword);
        init => OpenCodePassword = SettingsSecret.Unprotect(value);
    }

    [JsonPropertyName("OpenCodePassword"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlainOpenCodePassword
    {
        get => null;
        init
        {
            if (!string.IsNullOrEmpty(value) && OpenCodePassword.Length == 0)
            {
                OpenCodePassword = value;
                HadPlainSecrets = true;
            }
        }
    }

    /// <summary>The file had a secret in plain text (an older version): <see cref="Load"/> saves it again encrypted.</summary>
    [JsonIgnore]
    private bool HadPlainSecrets { get; init; }
    /// <summary>Session id; empty = the most recently used session.</summary>
    public string OpenCodeSession { get; init; } = "";

    public HammerMcp.Core.Agents.OpenCodeOptions ToOpenCodeOptions() => new()
    {
        Url = OpenCodeUrl,
        Username = OpenCodeUsername,
        Password = OpenCodePassword,
        Session = OpenCodeSession,
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveLogFolder => string.IsNullOrWhiteSpace(LogFolder) ? DefaultLogFolder : LogFolder;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
            if (settings.HadPlainSecrets)
                settings.Save(); // encrypt what an older version left in plain text
            return settings;
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
