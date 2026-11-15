using HammerMcp.Core.Agents;
using HammerMcp.Server;

namespace HammerMcp.App;

/// <summary>The OpenCode tab: send the user's Hammer chat lines into an OpenCode session (wakes the agent there).</summary>
public partial class FormMain
{
    private OpenCodeBridge? _openCode;
    private CheckBox _openCodeEnabled = null!;
    private TextBox _openCodeUrl = null!, _openCodeUser = null!, _openCodePassword = null!, _openCodeSession = null!;
    private Button _openCodeSave = null!, _openCodeTest = null!;
    private Label _openCodeResult = null!;

    private const string OpenCodeHelp =
        "When you write to the agent in Hammer (MCP > Chat, needs the adapter), the line also goes into your OpenCode "
        + "session as a message, so an agent that works in OpenCode answers without you switching windows. "
        + "If the agent is already waiting in wait_for_user, the line goes there instead.\r\n\r\n"
        + "OpenCode 2.x: the TUI talks to a background server, http://127.0.0.1:4096 by default (opencode service get). "
        + "\"opencode serve\" prints its password at start; for the background server set one with "
        + "\"opencode service set env OPENCODE_SERVER_PASSWORD <password>\" and \"opencode service restart\". "
        + "OpenCode 1.x: start it with --port 4096 (and OPENCODE_SERVER_PASSWORD if you want a password). "
        + "Session: empty = the session you used last. Test shows which session the lines go to.";

    private void InitOpenCodeTab()
    {
        _openCode = new OpenCodeBridge(_target.Control, _log);

        var page = new TabPage("OpenCode") { Padding = new Padding(6), UseVisualStyleBackColor = true };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = false };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _openCodeEnabled = new CheckBox { Text = "Send my Hammer chat lines to OpenCode", AutoSize = true };
        _openCodeUrl = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "http://127.0.0.1:4096" };
        _openCodeUser = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "opencode" };
        _openCodePassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "empty = no password" };
        _openCodeSession = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "empty = the most recently used session" };
        _openCodeTest = new Button { Text = "Test", AutoSize = true };
        _openCodeSave = new Button { Text = "Save", AutoSize = true };
        _openCodeResult = new Label { AutoSize = true, MaximumSize = new Size(560, 0) };
        var help = new TextBox
        {
            Text = OpenCodeHelp, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            BackColor = SystemColors.Window, TabStop = false,
        };

        grid.Controls.Add(_openCodeEnabled, 0, 0);
        grid.SetColumnSpan(_openCodeEnabled, 2);
        AddRow(grid, 1, "Server URL", _openCodeUrl);
        AddRow(grid, 2, "Username", _openCodeUser);
        AddRow(grid, 3, "Password", _openCodePassword);
        AddRow(grid, 4, "Session id", _openCodeSession);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.AddRange([_openCodeTest, _openCodeSave]);
        grid.Controls.Add(buttons, 1, 5);
        grid.Controls.Add(_openCodeResult, 1, 6);
        grid.Controls.Add(help, 0, 7);
        grid.SetColumnSpan(help, 2);
        for (var i = 0; i < 7; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(grid);
        tabControl.TabPages.Insert(tabControl.TabPages.IndexOf(tabPageSettings) + 1, page);

        ShowOpenCode(_settings);
        foreach (var c in new Control[] { _openCodeUrl, _openCodeUser, _openCodePassword, _openCodeSession })
            c.TextChanged += (_, _) => UpdateOpenCodeSave();
        _openCodeEnabled.CheckedChanged += (_, _) => UpdateOpenCodeSave();
        _openCodeSave.Click += (_, _) => SaveOpenCode();
        _openCodeTest.Click += async (_, _) => await TestOpenCodeAsync();
        ApplyOpenCode();
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void ShowOpenCode(AppSettings s)
    {
        _openCodeEnabled.Checked = s.OpenCodeEnabled;
        _openCodeUrl.Text = s.OpenCodeUrl;
        _openCodeUser.Text = s.OpenCodeUsername;
        _openCodePassword.Text = s.OpenCodePassword;
        _openCodeSession.Text = s.OpenCodeSession;
        UpdateOpenCodeSave();
    }

    private AppSettings OpenCodeFromForm() => _settings with
    {
        OpenCodeEnabled = _openCodeEnabled.Checked,
        OpenCodeUrl = _openCodeUrl.Text.Trim(),
        OpenCodeUsername = _openCodeUser.Text.Trim(),
        OpenCodePassword = _openCodePassword.Text,
        OpenCodeSession = _openCodeSession.Text.Trim(),
    };

    private void UpdateOpenCodeSave() => _openCodeSave.Enabled = OpenCodeFromForm() != _settings;

    private void SaveOpenCode()
    {
        var s = OpenCodeFromForm();
        if (s.OpenCodeEnabled && !Uri.TryCreate(s.OpenCodeUrl, UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "Server URL must be like http://127.0.0.1:4096.", "Hammer-MCP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settings = s;
        _settings.Save();
        ApplyOpenCode();
        UpdateOpenCodeSave();
        UpdateApplyButton();
    }

    private void ApplyOpenCode()
    {
        _openCode!.Options = _settings.OpenCodeEnabled ? _settings.ToOpenCodeOptions() : null;
        _log.Write(_settings.OpenCodeEnabled
            ? $"opencode bridge on: Hammer chat lines go to {_settings.OpenCodeUrl}"
              + (_settings.OpenCodeSession.Length > 0 ? $", session {_settings.OpenCodeSession}" : ", the most recent session")
            : "opencode bridge off");
    }

    private async Task TestOpenCodeAsync()
    {
        _openCodeTest.Enabled = false;
        _openCodeResult.Text = "Connecting...";
        try
        {
            _openCodeResult.Text = await OpenCodeBridge.TestAsync(OpenCodeFromForm().ToOpenCodeOptions());
        }
        catch (Exception e) when (e is OpenCodeException or HttpRequestException or TaskCanceledException
                                      or InvalidOperationException or UriFormatException or System.Text.Json.JsonException)
        {
            _openCodeResult.Text = $"Failed: {e.Message}";
        }
        finally
        {
            _openCodeTest.Enabled = true;
        }
    }
}
