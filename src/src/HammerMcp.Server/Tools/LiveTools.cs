using System.ComponentModel;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>Layer 2 of the adapter: what Hammer holds in memory right now (selection, 3D camera), read by the injected adapter.</summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class LiveTools(HammerTarget target)
{
    [McpServerTool(Name = "hammer_selection", ReadOnly = true)]
    [Description("What is selected in Hammer right now, read live from Hammer's memory by the injected adapter (Advanced mode, builds with a known memory layout - see hammer_status adapter.liveRead). Each item: kind (entity, solid, group, ...), entities with className, origin and keyValues, solids with their textures and owning entity, bounds (mins / maxs) of each and of the whole selection. Unsaved changes are included - no need to save. sent=true returns the selection the user last sent with MCP > Send selection to agent instead of the current one. Selecting a large part of a big map (e.g. Select All) can freeze Hammer for a long time while it redraws: ask the user for confirmation before you select much on a big map. \"Hammer is busy\" = its window did not answer in 3 s; wait instead of retrying in a loop.")]
    public async Task<object> Selection(bool sent = false, CancellationToken cancellationToken = default)
    {
        if (target.Adapter is not { } adapter)
            return new { error = "no adapter link" };
        if (sent)
            return adapter.SentSelection is { } s
                ? new { sentAt = s.At.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), selection = Plain(s.Selection) }
                : new { error = "the user has not sent a selection (MCP > Send selection to agent)" };
        return await Read(adapter, "get_selection", cancellationToken);
    }

    [McpServerTool(Name = "hammer_camera", ReadOnly = true)]
    [Description("Where Hammer's 3D view camera is and where it looks, read live by the injected adapter (Advanced mode, builds with a known memory layout): position, forward (unit vector), yaw and pitch in degrees, fov. Use it to place things where the user is looking.")]
    public Task<object> Camera(CancellationToken cancellationToken = default) =>
        target.Adapter is { } adapter ? Read(adapter, "get_camera", cancellationToken) : Task.FromResult<object>(new { error = "no adapter link" });

    private static async Task<object> Read(Adapter.AdapterLink adapter, string command, CancellationToken cancel)
    {
        try
        {
            var answer = await adapter.ReadLiveAsync(command, cancel);
            if ((string?)answer["type"] == "error")
                return new { error = (string?)answer["message"] ?? "the adapter could not read it" };
            return Plain(answer);
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException)
        {
            return new { error = e.Message };
        }
    }

    // the adapter's answer without the protocol fields
    private static JsonObject Plain(JsonObject answer)
    {
        var copy = (JsonObject)answer.DeepClone();
        copy.Remove("type");
        copy.Remove("id");
        return copy;
    }
}
