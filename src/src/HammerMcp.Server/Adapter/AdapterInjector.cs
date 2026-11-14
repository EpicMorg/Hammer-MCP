using System.Diagnostics;
using System.Runtime.Versioning;
using HammerMcp.Core.Registry;

namespace HammerMcp.Server.Adapter;

/// <summary>
/// Starts the native injector (hmcp-injector32/64.exe) for a Hammer process. Only after <see cref="InjectPolicy"/> allowed
/// it here; the injector checks the policy again itself (known editor exe, sha256 of the editor DLL from the same registry)
/// before it opens the process for writing. Only ever started by the user (the "Connect adapter" button).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AdapterInjector
{
    public sealed record Result(bool Ok, bool AlreadyLoaded, string Message);

    /// <summary>Folder with the injectors and adapter DLLs: <c>native\</c> next to the app.</summary>
    public static string NativeDir => Path.Combine(AppContext.BaseDirectory, "native");

    /// <summary>Why this Hammer cannot get an adapter (null = it can): policy, missing binaries.</summary>
    public static string? CannotInject(string exePath, HammerBuild? build)
    {
        var decision = InjectPolicy.Check(exePath, build);
        if (!decision.Allowed)
            return decision.Reason;
        var injector = InjectorPath(build!);
        if (!File.Exists(injector))
            return $"{Path.GetFileName(injector)} not found in {NativeDir}";
        var dll = AdapterPath(build!);
        return File.Exists(dll) ? null : $"{Path.GetFileName(dll)} not found in {NativeDir}";
    }

    public static async Task<Result> InjectAsync(int pid, string exePath, HammerBuild? build, CancellationToken cancel = default)
    {
        if (CannotInject(exePath, build) is { } why)
            return new Result(false, false, why);
        var start = new ProcessStartInfo(InjectorPath(build!))
        {
            ArgumentList = { "--pid", pid.ToString(), "--adapter", NativeDir },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the injector did not start");
        var stdout = process.StandardOutput.ReadToEndAsync(cancel);
        var stderr = process.StandardError.ReadToEndAsync(cancel);
        await process.WaitForExitAsync(cancel);
        var text = string.Join(" ", new[] { await stdout, await stderr }.Select(s => s.Trim()).Where(s => s.Length > 0));
        // exit codes: 0 injected, 2 usage, 3 refused by the policy, 4 error, 5 already injected (adapter answering)
        return process.ExitCode switch
        {
            0 => new Result(true, false, text),
            5 => new Result(true, true, text),
            3 => new Result(false, false, $"refused by the injector: {text}"),
            _ => new Result(false, false, $"injector failed (exit {process.ExitCode}): {text}"),
        };
    }

    /// <summary>One adapter DLL per bitness; it finds out itself which build it runs in (InjectAdapter is a support level).</summary>
    private static string AdapterPath(HammerBuild build) =>
        Path.Combine(NativeDir, build.Arch == "x64" ? "hmcp-adapter-x64.dll" : "hmcp-adapter-x86.dll");

    private static string InjectorPath(HammerBuild build) =>
        Path.Combine(NativeDir, build.Arch == "x64" ? "hmcp-injector64.exe" : "hmcp-injector32.exe");
}
