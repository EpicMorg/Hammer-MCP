using HammerMcp.Core.Registry;
using HammerMcp.Server.Tools;
using HammerMcp.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace HammerMcp.Server;

/// <summary>Builds the MCP HTTP server; the tray app starts and stops it.</summary>
public static class HammerMcpHost
{
    public static WebApplication Build(HammerMcpServerOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(_ => File.Exists(options.RegistryPath)
            ? HammerRegistry.Load(options.RegistryPath)
            : new HammerRegistry([]));
        builder.Services.AddSingleton<ShellChannel>();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<StatusTools>()
            .WithTools<EntityTools>();

        var app = builder.Build();
        app.MapMcp(options.Path);
        return app;
    }
}
