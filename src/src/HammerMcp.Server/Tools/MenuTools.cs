using System.ComponentModel;
using System.Runtime.Versioning;
using HammerMcp.Core.Registry;
using HammerMcp.Shell;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>Hammer main-menu commands via WM_COMMAND (same ids in every Source Hammer build).</summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class MenuTools(HammerTarget target, ActivityLog log)
{
    [McpServerTool(Name = "save_map")]
    [Description("File > Save in Hammer. After saving, the .vmf on disk is up to date and hammer_status shows the new mapversion.")]
    public MenuResult SaveMap() => Post("save", MenuCommands.Save);

    [McpServerTool(Name = "undo")]
    [Description("Edit > Undo in Hammer. Undoes entity creation, deletion and rotation made through the shell channel; keyvalue changes (set_keyvalue) are NOT in Hammer's undo history - restore the old value with set_keyvalue instead.")]
    public MenuResult Undo() => Post("undo", MenuCommands.Undo);

    [McpServerTool(Name = "redo")]
    [Description("Edit > Redo in Hammer.")]
    public MenuResult Redo() => Post("redo", MenuCommands.Redo);

    [McpServerTool(Name = "open_run_map_dialog")]
    [Description("Opens Hammer's Run Map dialog (F9) for the user; it does not start a compile by itself.")]
    public MenuResult RunMap() => Post("run map", MenuCommands.RunMap);

    [McpServerTool(Name = "open_check_for_problems")]
    [Description("Opens Hammer's Map > Check for Problems dialog for the user.")]
    public MenuResult CheckForProblems() => Post("check for problems", MenuCommands.CheckForProblems);

    private MenuResult Post(string name, int id)
    {
        var process = target.Current();
        if (process is null || process.MainWindow == 0)
            return new MenuResult(false, $"no Hammer is attached - {target.AttachHint}");
        if (process.IsSource2)
            return new MenuResult(false, "Source 2 Hammer (Qt, inside the game process) has no Win32 menu commands; ask the user to do it in Hammer");
        var ok = MenuChannel.Post(process.MainWindow, id);
        log.Write($"menu: {name} ({id}) -> {(ok ? "posted" : "failed")}");
        return new MenuResult(ok, ok ? $"{name} posted to '{process.MainTitle}'" : "PostMessage failed");
    }
}

public sealed record MenuResult(bool Ok, string Message);
