using HammerMcp.Server;

namespace HammerMcp.App;

/// <summary>
/// Approval mode: the agent's changes waiting for the user. Tick some and Apply selected, or Apply all / Reject all.
/// Opens by itself when a change arrives; the agent's tool call waits (up to 2 minutes) for the decision.
/// </summary>
internal sealed class FormPending : Form
{
    private readonly PendingChanges _changes;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false,
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 6, 6, 0),
        Text = "The agent wants to make these changes. Nothing happens in Hammer until you apply them.",
    };
    private readonly HashSet<string> _shown = []; // new changes come in ticked; the user's unticking is kept
    private readonly Button _applySelected = new() { Text = "Apply selected", Width = 110 };
    private readonly Button _applyAll = new() { Text = "Apply all", Width = 90 };
    private readonly Button _rejectAll = new() { Text = "Reject all", Width = 90 };
    private readonly Button _close = new() { Text = "Close", Width = 75 };

    public FormPending(PendingChanges changes, Icon? icon)
    {
        _changes = changes;
        Text = "Hammer-MCP: pending changes";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 320);
        MinimumSize = new Size(420, 220);
        ShowInTaskbar = true;

        _list.Columns.Add("Id", 50);
        _list.Columns.Add("Asked", 70);
        _list.Columns.Add("Change", 460);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        buttons.Controls.AddRange([_close, _rejectAll, _applyAll, _applySelected]);
        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(buttons);

        _applySelected.Click += (_, _) => Decide(applyChecked: true);
        _applyAll.Click += (_, _) => _changes.ApplyAll();
        _rejectAll.Click += (_, _) => _changes.RejectAll();
        _close.Click += (_, _) => Hide();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // keep the window (and what is ticked) for the next change
                Hide();
            }
        };
        _changes.Changed += () =>
        {
            if (IsHandleCreated)
                BeginInvoke(Fill);
        };
        Fill();
    }

    /// <summary>Apply the ticked changes; the unticked ones keep waiting.</summary>
    private void Decide(bool applyChecked)
    {
        var ticked = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag!).ToList();
        if (applyChecked && ticked.Count > 0)
            _changes.Decide(ticked, []);
    }

    private void Fill()
    {
        var pending = _changes.Pending();
        var ticked = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag!).ToHashSet();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var c in pending)
            _list.Items.Add(new ListViewItem([c.Id, c.Asked.ToString("HH:mm:ss"), c.Text]) { Tag = c.Id, Checked = ticked.Contains(c.Id) || !_shown.Contains(c.Id) });
        _shown.Clear();
        _shown.UnionWith(pending.Select(c => c.Id));
        _list.EndUpdate();
        _applySelected.Enabled = _applyAll.Enabled = _rejectAll.Enabled = pending.Count > 0;
        Text = pending.Count > 0 ? $"Hammer-MCP: pending changes ({pending.Count})" : "Hammer-MCP: no pending changes";
    }

    /// <summary>Brings the window up when a change arrives (without stealing focus from Hammer for long).</summary>
    public void Popup()
    {
        Fill();
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        TopMost = true;
        TopMost = false;
    }
}
