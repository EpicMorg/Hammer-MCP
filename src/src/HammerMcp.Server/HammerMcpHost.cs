using System.Runtime.Versioning;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Registry;
using HammerMcp.Server.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HammerMcp.Server;

/// <summary>Builds the MCP server: HTTP (the tray app) or stdio (hammer-mcp-cli, MCP clients that launch the server).</summary>
[SupportedOSPlatform("windows")]
public static class HammerMcpHost
{
    /// <summary>Services and tools shared by every transport; the caller adds the transport.</summary>
    public static IMcpServerBuilder AddServices(IServiceCollection services, HammerMcpServerOptions options, HammerTarget target, ActivityLog log)
    {
        services.AddSingleton(options);
        services.AddSingleton(target);
        services.AddSingleton(log);
        services.AddSingleton<EditJournal>();
        services.AddSingleton<GameData>();
        services.AddSingleton(_ => HammerRegistry.LoadDefault(options.RegistryPath));
        services.AddSingleton<CompileTools>();

        var mcp = services
            .AddMcpServer(mcp =>
            {
                mcp.ServerInfo = new() { Name = "hammer-mcp", Version = typeof(HammerMcpHost).Assembly.GetName().Version?.ToString(3) ?? "0" };
                mcp.ServerInstructions = AgentInstructions.Text;
            })
            .WithTools<StatusTools>()
            .WithTools<MapTools>();
        if (options.AllowsLiveEdits)
            mcp.WithTools<EntityTools>().WithTools<MenuTools>().WithTools<RunCompileTools>();
        mcp.WithTools<CompileTools>().WithTools<Source2Tools>();
        return mcp;
    }

    /// <summary>
    /// MCP over stdin / stdout for clients that start the server themselves. Logs go to stderr - stdout is the protocol.
    /// </summary>
    public static IHost BuildStdio(HammerMcpServerOptions options, HammerTarget target, ActivityLog log)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        AddServices(builder.Services, options, target, log).WithStdioServerTransport();
        return builder.Build();
    }

    public static WebApplication Build(HammerMcpServerOptions options, HammerTarget target, ActivityLog log)
    {
        if (!options.IsLoopback && string.IsNullOrEmpty(options.Token))
            throw new InvalidOperationException("a security token is required when listening beyond localhost");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");

        AddServices(builder.Services, options, target, log).WithHttpTransport();

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
