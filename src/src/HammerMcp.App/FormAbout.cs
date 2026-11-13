using System.Diagnostics;
using System.Reflection;

namespace HammerMcp.App;

public partial class FormAbout : Form
{
    private const string SourcesUrl = "https://github.com/EpicMorg/Hammer-MCP";
    private const string AuthorUrl = "https://epicm.org";
    private const int FirstYear = 2026;

    public FormAbout()
    {
        InitializeComponent();

        labelVersion.Text = $"Version {AppVersion}";
        var year = DateTime.Now.Year;
        labelLicense.Text = year > FirstYear ? $"(c) {FirstYear}-{year}, MIT" : $"(c) {FirstYear}, MIT";
        AcceptButton = buttonOK;
        CancelButton = buttonOK;
        buttonOK.DialogResult = DialogResult.OK;
        linkLabelSources.LinkClicked += (_, _) => OpenUrl(SourcesUrl);
        linkLabelSiteAuthor.LinkClicked += (_, _) => OpenUrl(AuthorUrl);
    }

    /// <summary>
    /// Product version of hammer-mcp.exe (its version resource), without the "+commit" build metadata, e.g. "0.2.0";
    /// falls back to the assembly's informational version.
    /// </summary>
    public static string AppVersion
    {
        get
        {
            var file = Environment.ProcessPath is { } exe && File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
            var version = !string.IsNullOrWhiteSpace(file) ? file
                : typeof(FormAbout).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? Application.ProductVersion;
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
