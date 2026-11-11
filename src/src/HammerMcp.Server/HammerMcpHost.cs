using System.Runtime.Versioning;
using HammerMcp.Core.Registry;
using HammerMcp.Server.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HammerMcp.Server;

/// <summary>Builds the MCP HTTP server; the tray app starts and stops it.</summary>
[SupportedOSPlatform("windows")]
public static class HammerMcpHost
{
    public static WebApplication Build(HammerMcpServerOptions options, HammerTarget target, ActivityLog log)
    {
        if (!options.IsLoopback && string.IsNullOrEmpty(options.Token))
            throw new InvalidOperationException("a security token is required when listening beyond localhost");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(target);
        builder.Services.AddSingleton(log);
        builder.Services.AddSingleton(_ => File.Exists(options.RegistryPath)
            ? HammerRegistry.Load(options.RegistryPath)
            : new HammerRegistry([]));

        var mcp = builder.Services
            .AddMcpServer(mcp =>
            {
                mcp.ServerInfo = new() { Name = "hammer-mcp", Version = typeof(HammerMcpHost).Assembly.GetName().Version?.ToString(3) ?? "0" };
                mcp.ServerInstructions = AgentInstructions.Text;
            })
            .WithHttpTransport()
            .WithTools<StatusTools>();
        if (options.AllowsLiveEdits)
            mcp.WithTools<EntityTools>().WithTools<MenuTools>();

        var app = builder.Build();
        if (!string.IsNullOrEmpty(options.Token))
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.Authorization.ToString() != $"Bearer {options.Token}")
                {
                    log.Write($"rejected request from {context.Connection.RemoteIpAddress}: bad or missing token");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next(context);
            });
        app.MapMcp(options.Path);
        return app;
    }
}
