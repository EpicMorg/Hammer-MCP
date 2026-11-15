using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Pe;
using HammerMcp.Core.Registry;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class StatusTools(HammerRegistry registry, HammerTarget target, HammerMcpServerOptions options, ActivityLog log)
{
    private static readonly string[] EditorDlls = ["hammerplusplus_dll.dll", "hammer_dll.dll", "hammer.dll"];

    [McpServerTool(Name = "hammer_status", ReadOnly = true)]
    [Description("Which Hammer the server works with (process, exe, known build), which map is open, its saved mapversion, and what the agent may do (mode). Also: agentPaused (the user paused you in Hammer - no edits until resumed) and userRequests (what the user wrote to you in Hammer's MCP > Chat - do it, and answer with say_to_user). Call it at the start and between steps.")]
    public HammerStatus Status()
    {
        var process = target.Current();
        if (process is null)
            return new HammerStatus(Running: false, Mode: options.Mode.ToString(), LiveEdits: false,
                Note: $"no Hammer is attached - {target.AttachHint}");

        var mapPath = process.OpenMapPath;
        int? mapVersion = mapPath is not null && File.Exists(mapPath) && !mapPath.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase)
            ? VmfVersionInfo.Read(mapPath).MapVersion : null;
        var (build, dll) = Identify(registry, process.ExePath);
        return new HammerStatus(
            Running: true,
            Mode: options.Mode.ToString(),
            LiveEdits: options.AllowsShell && process.HasShellChannel && !target.Control.Paused,
            AgentPaused: target.Control.Paused,
            UserRequests: Requests(),
            Approval: target.Changes.AskUser ? "ask" : "auto",
            PendingChanges: target.Changes.Count,
            Adapter: target.Adapter?.Hello is { } a ? new AdapterStatus(a.Adapter, a.Version, a.Build) : null,
            ProcessId: process.ProcessId,
            ExePath: process.ExePath,
            Arch: PeHeader.TryRead(process.ExePath)?.Arch,
            EditorDll: dll,
            BuildId: build?.Id,
            Family: build?.Family,
            ShellChannel: process.HasShellChannel,
            OpenMap: mapPath,
            SavedMapVersion: mapVersion,
            WindowTitle: process.MainTitle,
            // all visible windows of the process (when the open map is not found, the title format is usually the reason)
            WindowTitles: mapPath is null ? process.WindowTitles : null,
            CommandLine: process.CommandLine,
            // -insecure = started without VAC (for the inject policy); reported, not trusted on its own
            Insecure: process.CommandLine is { } cl ? cl.Contains("-insecure", StringComparison.OrdinalIgnoreCase) : null,
            // unknown build: report the hash so it can be added to registry/hammers.json
            EditorSha256: build is null && dll is not null ? Sha256Of(dll) : null,
            Note: process.IsSource2
                ? "Source 2 Hammer: read-only. Map tools read the saved .vmap, fgd_* use game/<mod>/<mod>.fgd; no live edits, no menu commands."
                : null);
    }

    /// <summary>Lines the user wrote in Hammer (MCP > Chat) since the last call; each is delivered once.</summary>
    private IReadOnlyList<string>? Requests() => target.Control.TakeRequests() is { Count: > 0 } r ? r : null;

    [McpServerTool(Name = "user_requests", ReadOnly = true)]
    [Description("What the user wrote to you in Hammer's chat (MCP > Chat) since you last looked, and whether they paused you. Answer with say_to_user. recentChat = the last 10 chat lines from both sides (already seen ones too), for context. Cheap: call it between steps of a longer task. Each request is returned once (hammer_status returns them too).")]
    public object UserRequests() => new
    {
        agentPaused = target.Control.Paused,
        userRequests = Requests() ?? [],
        // the last lines of the chat (both sides): a line taken by a call whose answer got lost is still visible here
        recentChat = target.Control.ChatHistory().TakeLast(10)
            .Select(l => $"{l.At.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} {l.From}: {l.Text}"),
    };

    [McpServerTool(Name = "wait_for_user", ReadOnly = true)]
    [Description("Wait until the user writes to you in Hammer's chat (MCP > Chat) or pauses / resumes you, up to timeoutSeconds (default 120, max 600). Use it after you asked the user something there with say_to_user, or when they want to work with you through that chat. Returns the same as user_requests; timedOut = true if nothing came - then ask again, wait again, or finish. The Hammer chat is a side channel: if the user may answer in your own window instead, tell them both are fine.")]
    public async Task<object> WaitForUser(int timeoutSeconds = 120, CancellationToken cancellationToken = default)
    {
        var control = target.Control;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged() => changed.TrySetResult();
        using var waiting = control.BeginWait(); // the OpenCode bridge leaves a new line to this call
        control.Changed += OnChanged; // a new chat line or a pause / resume
        try
        {
            if (control.PendingRequests == 0)
            {
                var wasPaused = control.Paused;
                try
                {
                    await changed.Task.WaitAsync(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 600)), cancellationToken);
                }
                catch (TimeoutException)
                {
                    return new { timedOut = true, agentPaused = control.Paused, userRequests = Array.Empty<string>() };
                }
                if (control.PendingRequests == 0 && control.Paused == wasPaused)
                    return new { timedOut = false, agentPaused = control.Paused, userRequests = Array.Empty<string>() };
            }
            return UserRequests();
        }
        finally
        {
            control.Changed -= OnChanged;
        }
    }

    [McpServerTool(Name = "say_to_user", ReadOnly = true)]
    [Description("Write to the user in Hammer's chat window (MCP > Chat, with the adapter connected; the app's log shows it too). Use it to answer what they wrote (user_requests), to ask a question or to report progress of a longer task. Plain text, short.")]
    public object SayToUser(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new { ok = false, error = "empty text" };
        target.Control.Say(text.Trim());
        log.Write($"agent says: {text.Trim()}");
        return target.Adapter is { Connected: true }
            ? new { ok = true, shownIn = "Hammer's chat (MCP > Chat) and the app's log" }
            : new { ok = true, shownIn = "the app's log only: no adapter is connected, so Hammer has no chat window - also tell the user in your own reply" };
    }

    [McpServerTool(Name = "pending_changes", ReadOnly = true)]
    [Description("Approval mode: whether your edits wait for the user (approval ask / auto), which of your changes are still waiting, and what happened to the recent ones (applied / rejected / failed, with Hammer's answer). Use it after an edit returned 'waiting for the user's approval' instead of repeating the edit.")]
    public object PendingChanges() => new
    {
        approval = target.Changes.AskUser ? "ask" : "auto",
        waiting = target.Changes.Pending().Select(c => new { c.Id, c.Text, asked = c.Asked.ToString("HH:mm:ss") }),
        decided = target.Changes.RecentlyDecided().Reverse().Take(20)
            .Select(c => new { c.Id, c.Text, state = c.State.ToString().ToLowerInvariant(), c.Result }),
    };

    [McpServerTool(Name = "map_summary", ReadOnly = true)]
    [Description("Summary of the map open in Hammer, read from the SAVED .vmf: mapversion, editor build, entity and brush counts, entity classes with counts, skybox, visgroups. Unsaved edits are not included - call save_map first.")]
    public object MapSummary([Description("Read the newest autosave if it is newer than the saved map (Hammer++, stock, Strata); Source 2: forces an autosave first, i.e. reads Hammer's current unsaved state")] bool fromAutosave = false)
    {
        var (map, source) = target.MapSource(fromAutosave);
        if (map is null)
            return new { error = "no saved map open in the attached Hammer" };
        var file = new FileInfo(map);
        return new { path = map, source, saved = file.LastWriteTime, sizeBytes = file.Length, summary = VmfSummary.Read(map) };
    }

    [McpServerTool(Name = "list_entities", ReadOnly = true)]
    [Description("Entities of the map open in Hammer, from the SAVED .vmf: id, class, origin, targetname, hidden, brush entity. Filters are optional and combined; results are capped by 'limit'.")]
    public object ListEntities(
        [Description("Exact class name, e.g. light")] string? className = null,
        [Description("Substring of targetname")] string? nameContains = null,
        [Description("Only entities within 'radius' units of x y z")] float? x = null, float? y = null, float? z = null,
        float radius = 256,
        int limit = 200,
        [Description("Read the newest autosave if it is newer than the saved map (Hammer++, stock, Strata); Source 2: forces an autosave first, i.e. reads Hammer's current unsaved state")] bool fromAutosave = false)
    {
        var (map, source) = target.MapSource(fromAutosave);
        if (map is null)
            return new { error = "no saved map open in the attached Hammer (or nothing attached)" };
        var center = x is { } cx && y is { } cy && z is { } cz ? new System.Numerics.Vector3(cx, cy, cz) : (System.Numerics.Vector3?)null;
        var query = MapEntities.Read(map).AsEnumerable();
        if (className is not null)
            query = query.Where(e => string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase));
        if (nameContains is not null)
            query = query.Where(e => e.TargetName?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true);
        if (center is { } c)
            query = query.Where(e => (e.Origin ?? e.BoundsCenter) is { } o && System.Numerics.Vector3.Distance(o, c) <= radius);
        var all = query.ToList();
        return new
        {
            map,
            source,
            total = all.Count,
            entities = all.Take(Math.Clamp(limit, 1, 2000)).Select(e => new
            {
                e.Id,
                className = e.ClassName,
                origin = e.Origin is { } o ? new[] { o.X, o.Y, o.Z } : null,
                // brush entities without an origin are addressed by the centre of their brushes
                center = e.Origin is null && e.BoundsCenter is { } bc ? new[] { bc.X, bc.Y, bc.Z } : null,
                targetName = e.TargetName,
                e.Hidden,
                brushEntity = e.HasBrushes,
            }),
        };
    }

    /// <summary>Finds the editor DLL next to the exe and looks its sha256 up in the registry.</summary>
    public static (HammerBuild? Build, string? Dll) Identify(HammerRegistry registry, string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null)
            return (null, null);
        foreach (var candidate in EditorDllCandidates(exePath, dir))
        {
            if (File.Exists(candidate))
                return (registry.FindBySha256(Sha256Of(candidate)), candidate);
        }
        // GoldSrc Worldcraft / Hammer 3.x: the editor is the exe itself
        return File.Exists(exePath) && registry.FindBySha256(Sha256Of(exePath)) is { } own ? (own, exePath) : (null, null);
    }

    /// <summary>
    /// The editor DLL that belongs to THIS exe. Several Hammers can share one bin folder (Bestiarium: Slammin's
    /// hammer.exe + hammer_dll.dll and Hammer++'s hammerplusplus.exe + hammerplusplus/bin/hammerplusplus_dll.dll),
    /// so the DLL is chosen by the exe name, never just by what lies around.
    /// </summary>
    public static IEnumerable<string> EditorDllCandidates(string exePath, string dir)
    {
        if (Path.GetFileNameWithoutExtension(exePath).StartsWith("hammerplusplus", StringComparison.OrdinalIgnoreCase))
            return [Path.Combine(dir, EditorDlls[0]), Path.Combine(dir, "hammerplusplus", "bin", EditorDlls[0])];
        // stock / Slammin / Black Mesa / Strata: hammer_dll.dll next to the exe; Source 2: bin/win64/tools/hammer.dll
        return [Path.Combine(dir, EditorDlls[1]), Path.Combine(dir, EditorDlls[2]), Path.Combine(dir, "tools", EditorDlls[2])];
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public sealed record HammerStatus(
    bool Running,
    string Mode,
    bool LiveEdits,
    int? ProcessId = null,
    string? ExePath = null,
    string? Arch = null,
    string? EditorDll = null,
    string? BuildId = null,
    string? Family = null,
    bool ShellChannel = false,
    string? OpenMap = null,
    int? SavedMapVersion = null,
    string? WindowTitle = null,
    IReadOnlyList<string>? WindowTitles = null,
    string? CommandLine = null,
    bool? Insecure = null,
    string? EditorSha256 = null,
    string? Note = null,
    bool AgentPaused = false,
    IReadOnlyList<string>? UserRequests = null,
    AdapterStatus? Adapter = null,
    string Approval = "auto",
    int PendingChanges = 0);

/// <summary>The injected adapter the server is connected to (Hammer shows an MCP menu).</summary>
public sealed record AdapterStatus(string Id, string Version, string Build);
