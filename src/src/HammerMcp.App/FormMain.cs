using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Pe;
using HammerMcp.Core.Registry;
using HammerMcp.Server;
using HammerMcp.Server.Tools;
using HammerMcp.Shell;
using Microsoft.AspNetCore.Builder;

namespace HammerMcp.App;

public partial class FormMain : Form
{
    private const int MaxLogChars = 200_000;

    private static readonly Dictionary<McpMode, string> ModeHelp = new()
    {
        [McpMode.Auto] = "Best available for the attached Hammer: live edits through the shell channel and menu commands "
                         + "where a shell channel exists, otherwise read-only. Source 2 (CS2, Dota 2, HL:A): read-only "
                         + "from the .vmap and game files - its Hammer has no shell channel and no Win32 menus.",
        [McpMode.Basic] = "Read-only: the agent sees Hammer status and map files, but cannot change anything in Hammer. "
                          + "Works the same for Source 1, Source 2 (.vmap) and GoldSrc maps.",
        [McpMode.Shell] = "Live edits through Hammer's shell channel (create / keyvalue / rotate / delete entities) "
                          + "and menu commands (save, undo, redo, run map, check for problems). The map must be saved.",
        [McpMode.Advanced] = "Expert mode with an injected adapter (live read, selection, camera, geometry). "
                             + "Only ever into a standalone Hammer exe of a known build - never into a game process "
                             + "(VAC / anti-cheat), so never for Source 2. Not implemented yet - works as Shell.",
    };

    private readonly HammerTarget _target = new();
    private readonly ActivityLog _log = new();
    private readonly HammerRegistry _registry = LoadRegistry();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayStartStop;
    private readonly Icon _iconStarted = LoadIcon("hammer-mcp-started.ico");
    private readonly Icon _iconStopped = LoadIcon("hammer-mcp-stopped.ico");

    private readonly VmfSummaryCache _mapCache = new();
    private (int Pid, Image? Icon)? _processIcon;

    private AppSettings _settings = AppSettings.Load();
    private WebApplication? _server;
    private bool _exiting;

    public FormMain()
    {
        InitializeComponent();
        Text = $"Hammer MCP Server {FormAbout.AppVersion}";

        _trayStartStop = new ToolStripMenuItem("Start server", null, async (_, _) => await ToggleServerAsync());
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add(_trayStartStop);
        trayMenu.Items.Add("Copy client config", null, (_, _) => CopyClientConfig());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _tray = new NotifyIcon { Icon = _iconStopped, Text = "Hammer-MCP (stopped)", ContextMenuStrip = trayMenu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _log.Line += AppendLog;
        WireEvents();
        FillAddresses();
        comboBoxMCPMode.DataSource = Enum.GetValues<McpMode>();
        ShowSettings(_settings);
        UpdateApplyButton();
        RefreshProcessList();
        UpdateAttachUi();
        _log.Write($"registry: {_registry.Builds.Count} known Hammer builds");
    }

    private void WireEvents()
    {
        buttonServerStartStop.Click += async (_, _) => await ToggleServerAsync();
        buttonAttachOrDetach.Click += (_, _) => ToggleAttach();
        checkBoxShowProcess.CheckedChanged += (_, _) => RefreshProcessList();
        comboBoxProcessList.DropDown += (_, _) => RefreshProcessList();
        comboBoxMCPMode.SelectedIndexChanged += (_, _) =>
            textBoxMCPModeHelpText.Text = comboBoxMCPMode.SelectedItem is McpMode m ? ModeHelp[m] : "";
        buttonGenerateSecurityToken.Click += (_, _) => textBoxSecurityToken.Text = NewToken();
        buttonCopySecurityToken.Click += (_, _) => CopyText(textBoxSecurityToken.Text, "token");
        buttonApply.Click += async (_, _) => await ApplySettingsAsync();
        button1.Click += (_, _) => ShowSettings(new AppSettings());
        comboBoxMCPMode.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        comboBoxAdaptersAndAdresses.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        textBoxPort.TextChanged += (_, _) => UpdateApplyButton();
        textBoxSecurityToken.TextChanged += (_, _) => UpdateApplyButton();
        exitToolStripMenuItem.Click += async (_, _) => await ExitAsync();
        aboutToolStripMenuItem.Click += (_, _) =>
        {
            using var about = new FormAbout();
            about.ShowDialog(this);
        };
        _refreshTimer.Tick += (_, _) => RefreshAttached();
        FormClosing += OnFormClosing;
    }

    // ---- process / attach ------------------------------------------------------------------------------------

    private sealed record ProcessItem(HammerProcess Process)
    {
        public override string ToString() =>
            $"{Process.Name} ({Process.ProcessId}){(Process.HasShellChannel ? " [shell]" : "")} - {Process.MainTitle}";
    }

    private void RefreshProcessList()
    {
        var selectedPid = (comboBoxProcessList.SelectedItem as ProcessItem)?.Process.ProcessId ?? _target.AttachedPid;
        var items = HammerProcess.List(checkBoxShowProcess.Checked).Select(p => new ProcessItem(p)).ToArray();
        comboBoxProcessList.BeginUpdate();
        comboBoxProcessList.Items.Clear();
        comboBoxProcessList.Items.AddRange(items);
        comboBoxProcessList.SelectedItem = items.FirstOrDefault(i => i.Process.ProcessId == selectedPid) ?? items.FirstOrDefault();
        comboBoxProcessList.EndUpdate();
    }

    private void ToggleAttach()
    {
        if (_target.AttachedPid is not null)
        {
            _log.Write($"detached from pid {_target.AttachedPid}");
            _target.Detach();
        }
        else if (comboBoxProcessList.SelectedItem is ProcessItem item)
        {
            _target.Attach(item.Process.ProcessId);
            _log.Write($"attached to {item.Process.Name} ({item.Process.ProcessId}) {item.Process.ExePath}"
                       + (item.Process.HasShellChannel ? ""
                           : item.Process.IsSource2 ? " - Source 2 Hammer: read-only from map and game files"
                           : " - no shell channel: read-only + menu commands"));
        }
        UpdateAttachUi();
    }

    private void UpdateAttachUi()
    {
        var attached = _target.AttachedPid is not null;
        buttonAttachOrDetach.Text = attached ? "Detach" : "Attach";
        comboBoxProcessList.Enabled = !attached;
        checkBoxShowProcess.Enabled = !attached;
        groupBoxMetaInfoFromHammer.Enabled = attached;
        _refreshTimer.Enabled = attached;
        UpdateStartButton();
        RefreshAttached();
    }

    private void RefreshAttached()
    {
        if (_target.AttachedPid is not { } pid)
        {
            labelProcessName.Text = "<not attached>";
            UpdateProcessImage(null);
            dataGridViewMetaInfoFromHammer.Rows.Clear();
            SetStatus("Process not selected", Properties.Resources.warning_circle_16);
            return;
        }

        var process = HammerProcess.Refresh(pid);
        if (process is null)
        {
            _log.Write($"process {pid} exited - detached");
            _target.Detach();
            UpdateAttachUi();
            SetStatus($"Process {pid} exited", Properties.Resources.deny_circle_16);
            return;
        }

        var (build, dll) = StatusTools.Identify(_registry, process.ExePath);
        labelProcessName.Text = process.MainTitle;
        UpdateProcessImage(process);
        var who = $"{process.Name} ({process.ProcessId})";
        if (process.HasShellChannel)
            SetStatus($"Attached to {who}, shell channel OK", Properties.Resources.check_circle_16);
        else if (process.IsSource2)
            SetStatus($"Attached to {who}: Source 2 Hammer - read-only (maps and game files; live channel via VConsole planned)",
                Properties.Resources.warning_circle_16);
        else if (dll is not null || process.IsKnownEditorName)
            SetStatus($"Attached to {who}: map editor without shell channel (read-only + menu)", Properties.Resources.warning_circle_16);
        else
            SetStatus($"Attached to {who}: not a Hammer editor", Properties.Resources.deny_circle_16);
        ShowMetaInfo(process, build, dll);
    }

    /// <summary>Icon of the attached exe; otherwise green while the server runs, red when it is stopped.</summary>
    private void UpdateProcessImage(HammerProcess? process)
    {
        if (process is not null && _processIcon?.Pid != process.ProcessId)
            _processIcon = (process.ProcessId, ExtractExeIcon(process.ExePath));
        var image = process is not null ? _processIcon?.Icon : null;
        image ??= _server is not null ? Properties.Resources.hammer_mcp_green_48 : Properties.Resources.hammer_mcp_red_48;
        if (!ReferenceEquals(pictureBoxProcessImage.Image, image))
            pictureBoxProcessImage.Image = image;
    }

    private static Image? ExtractExeIcon(string exePath)
    {
        try
        {
            using var icon = Icon.ExtractIcon(exePath, 0, 48);
            return icon?.ToBitmap();
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ShowMetaInfo(HammerProcess process, HammerBuild? build, string? dll)
    {
        var pe = PeHeader.TryRead(process.ExePath);
        var rows = new List<(string Key, string? Value)>
        {
            ("Process", $"{process.Name} ({process.ProcessId})"),
            ("Executable", process.ExePath),
            ("Command line", process.CommandLine),
            ("Architecture", pe?.Arch),
            ("Linked", pe?.Linked.ToString("yyyy-MM-dd")),
            ("Editor DLL", dll),
            ("Known build", build is null ? (dll is null ? null : "unknown (not in registry)") : $"{build.Id} ({build.Family})"),
            ("Shell channel", process.HasShellChannel ? $"yes (0x{process.ShellWindow:X})" : "no"),
            ("Main window", process.MainWindow == 0 ? null : $"0x{process.MainWindow:X}"),
            ("Open map", process.OpenMapPath),
        };
        AddMapRows(rows, process.OpenMapPath);

        var grid = dataGridViewMetaInfoFromHammer;
        if (grid.Rows.Count != rows.Count)
        {
            grid.Rows.Clear();
            foreach (var (key, value) in rows)
                grid.Rows.Add(key, value ?? "-");
            return;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            grid.Rows[i].Cells[0].Value = rows[i].Key;
            grid.Rows[i].Cells[1].Value = rows[i].Value ?? "-";
        }
    }

    /// <summary>Map rows from the saved .vmf (re-read only when the file changes).</summary>
    private void AddMapRows(List<(string Key, string? Value)> rows, string? mapPath)
    {
        if (mapPath is null || !mapPath.EndsWith(".vmf", StringComparison.OrdinalIgnoreCase) || !File.Exists(mapPath))
            return;
        VmfSummary? map;
        try
        {
            map = _mapCache.Get(mapPath);
        }
        catch (IOException e)
        {
            rows.Add(("Map", $"cannot read: {e.Message}"));
            return;
        }
        if (map is null)
            return;
        var file = new FileInfo(mapPath);
        rows.Add(("Map: saved", $"{file.LastWriteTime:yyyy-MM-dd HH:mm:ss}, {file.Length / 1024.0 / 1024.0:0.0} MiB"));
        rows.Add(("Map: mapversion", map.MapVersion?.ToString()));
        rows.Add(("Map: editor", $"version {map.EditorVersion}, build {map.EditorBuild}, format {map.FormatVersion}{(map.Prefab ? ", prefab" : "")}"));
        rows.Add(("Map: entities", $"{map.Entities} (point {map.PointEntities}, brush {map.BrushEntities}), {map.EntityClasses} classes"));
        rows.Add(("Map: brushes", $"{map.Brushes} (world {map.WorldBrushes})"));
        rows.Add(("Map: top classes", string.Join(", ", map.Classes.Take(5).Select(c => $"{c.Key} {c.Value}"))));
        rows.Add(("Map: skybox", map.SkyName));
        rows.Add(("Map: visgroups / hidden", $"{map.Visgroups} / {map.HiddenObjects}"));
    }

    private void SetStatus(string text, Image icon)
    {
        toolStripStatusAttaching.Text = text;
        toolStripStatusAttaching.Image = icon;
    }

    // ---- server ----------------------------------------------------------------------------------------------

    /// <summary>Start needs an attached Hammer; Stop is always available.</summary>
    private void UpdateStartButton()
    {
        var canToggle = _server is not null || _target.AttachedPid is not null;
        buttonServerStartStop.Enabled = canToggle;
        _trayStartStop.Enabled = canToggle;
    }

    private async Task ToggleServerAsync()
    {
        if (_server is null && _target.AttachedPid is null)
            return;
        if (_server is null)
            await StartServerAsync();
        else
            await StopServerAsync();
    }

    private async Task StartServerAsync()
    {
        var options = _settings.ToServerOptions();
        try
        {
            _server = HammerMcpHost.Build(options, _target, _log);
            await _server.StartAsync();
        }
        catch (Exception e)
        {
            _server = null;
            _log.Write($"server failed to start: {e.Message}");
            MessageBox.Show(this, e.Message, "Hammer-MCP: server failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _log.Write($"server started: {options.Url} (mode {options.Mode}{(string.IsNullOrEmpty(options.Token) ? ", no token" : ", token required")})");
        UpdateServerUi(options);
    }

    private async Task StopServerAsync()
    {
        if (_server is null)
            return;
        await _server.StopAsync();
        await _server.DisposeAsync();
        _server = null;
        _log.Write("server stopped");
        UpdateServerUi(null);
    }

    private void UpdateServerUi(HammerMcpServerOptions? running)
    {
        var started = running is not null;
        buttonServerStartStop.Text = started ? "Stop" : "Start";
        _trayStartStop.Text = started ? "Stop server" : "Start server";
        _tray.Icon = started ? _iconStarted : _iconStopped;
        Icon = started ? _iconStarted : _iconStopped;
        var text = started ? $"Hammer-MCP {running!.Url}" : "Hammer-MCP (stopped)";
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon limit
        RefreshAttached(); // the process image falls back to green/red by server state
        UpdateStartButton();
    }

    private void CopyClientConfig()
    {
        var options = _settings.ToServerOptions();
        var header = string.IsNullOrEmpty(options.Token) ? "" : $" --header \"Authorization: Bearer {options.Token}\"";
        CopyText($"claude mcp add --transport http hammer {options.Url}{header}", "client config");
    }

    // ---- settings --------------------------------------------------------------------------------------------

    private sealed record AddressItem(string Address, string Label)
    {
        public override string ToString() => Label;
    }

    private void FillAddresses()
    {
        var items = new List<AddressItem>
        {
            new("127.0.0.1", "localhost (127.0.0.1)"),
            new("0.0.0.0", "All interfaces (0.0.0.0)"),
        };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip.Address))
                    items.Add(new AddressItem(ip.Address.ToString(), $"{ip.Address} ({nic.Name})"));
            }
        }
        comboBoxAdaptersAndAdresses.Items.Clear();
        comboBoxAdaptersAndAdresses.Items.AddRange([.. items]);
    }

    private void ShowSettings(AppSettings settings)
    {
        comboBoxMCPMode.SelectedItem = settings.Mode;
        var address = comboBoxAdaptersAndAdresses.Items.Cast<AddressItem>().FirstOrDefault(a => a.Address == settings.BindAddress);
        if (address is null)
        {
            address = new AddressItem(settings.BindAddress, $"{settings.BindAddress} (not available now)");
            comboBoxAdaptersAndAdresses.Items.Add(address);
        }
        comboBoxAdaptersAndAdresses.SelectedItem = address;
        textBoxPort.Text = settings.Port.ToString();
        textBoxSecurityToken.Text = settings.Token;
    }

    /// <summary>Settings as currently shown on the Settings tab (token trimmed; port 0 if not a number).</summary>
    private AppSettings SettingsFromForm() => new()
    {
        Mode = comboBoxMCPMode.SelectedItem is McpMode m ? m : McpMode.Auto,
        BindAddress = (comboBoxAdaptersAndAdresses.SelectedItem as AddressItem)?.Address ?? "127.0.0.1",
        Port = int.TryParse(textBoxPort.Text, out var port) ? port : 0,
        Token = textBoxSecurityToken.Text.Trim(),
    };

    /// <summary>Apply is enabled only when the form differs from the saved settings.</summary>
    private void UpdateApplyButton() => buttonApply.Enabled = SettingsFromForm() != _settings;

    private async Task ApplySettingsAsync()
    {
        if (!int.TryParse(textBoxPort.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Port must be a number 1-65535.", "Hammer-MCP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var address = (comboBoxAdaptersAndAdresses.SelectedItem as AddressItem)?.Address ?? "127.0.0.1";
        var token = textBoxSecurityToken.Text.Trim();
        if (address != "127.0.0.1" && token.Length == 0)
        {
            token = NewToken();
            textBoxSecurityToken.Text = token;
            _log.Write("a security token was generated: it is required when listening beyond localhost");
        }

        _settings = new AppSettings
        {
            Mode = comboBoxMCPMode.SelectedItem is McpMode m ? m : McpMode.Auto,
            BindAddress = address,
            Port = port,
            Token = token,
        };
        _settings.Save();
        UpdateApplyButton();
        _log.Write($"settings saved: {address}:{port}, mode {_settings.Mode}");

        if (_server is not null)
        {
            await StopServerAsync();
            await StartServerAsync();
        }
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---- logs / tray / helpers -------------------------------------------------------------------------------

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }
        if (textBoxLiveLogs.TextLength > MaxLogChars)
            textBoxLiveLogs.Text = textBoxLiveLogs.Text[(MaxLogChars / 2)..];
        textBoxLiveLogs.AppendText(line + Environment.NewLine);
    }

    private void CopyText(string text, string what)
    {
        if (string.IsNullOrEmpty(text))
            return;
        Clipboard.SetText(text);
        _log.Write($"{what} copied to clipboard");
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason != CloseReason.UserClosing)
            return;
        e.Cancel = true; // the close button hides to tray; File > Exit or the tray menu quits
        Hide();
        _tray.ShowBalloonTip(2000, "Hammer-MCP", "Still running in the tray.", ToolTipIcon.Info);
    }

    private async Task ExitAsync()
    {
        _exiting = true;
        await StopServerAsync();
        _tray.Visible = false;
        _tray.Dispose();
        Close();
    }

    private static HammerRegistry LoadRegistry() =>
        HammerRegistry.LoadDefault(Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json"));

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(FormMain).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"embedded icon {name} not found");
        return new Icon(stream);
    }
}
