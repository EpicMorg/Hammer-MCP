using HammerMcp.Server;
using Microsoft.AspNetCore.Builder;

namespace HammerMcp.App;

/// <summary>
/// Minimal tray host: start/stop the MCP server. Placeholder until the real UI (WinForms + WebView2) is drawn.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _startStop;
    private readonly HammerMcpServerOptions _options = new()
    {
        RegistryPath = Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json"),
    };
    private WebApplication? _server;

    public TrayContext()
    {
        _startStop = new ToolStripMenuItem("Start", null, async (_, _) => await ToggleAsync());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_startStop);
        menu.Items.Add("Copy client config", null, (_, _) => Clipboard.SetText(
            $"claude mcp add --transport http hammer {_options.Url}"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Hammer-MCP (stopped)",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private async Task ToggleAsync()
    {
        if (_server is null)
        {
            _server = HammerMcpHost.Build(_options);
            await _server.StartAsync();
            _startStop.Text = "Stop";
            _icon.Text = $"Hammer-MCP {_options.Url}";
        }
        else
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
            _server = null;
            _startStop.Text = "Start";
            _icon.Text = "Hammer-MCP (stopped)";
        }
    }

    private async Task ExitAsync()
    {
        if (_server is not null)
            await ToggleAsync();
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    }
}
