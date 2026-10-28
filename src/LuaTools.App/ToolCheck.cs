using System.Diagnostics;
using System.Runtime.InteropServices;
using LuaTools.Core;

namespace LuaTools.App;

/// <summary>
/// Networked verification of the only external dependency: the GitHub release
/// asset is fetched with the same code the UI uses, then executed to prove the
/// self-contained binary starts on this host. Runs headlessly in CI.
/// </summary>
internal static class ToolCheck
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("luatools tool check");
        Console.WriteLine($"  os:      {RuntimeInformation.OSDescription}");
        Console.WriteLine($"  arch:    {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"  rid tag: {KnownTools.RuntimeTag}");
        Console.WriteLine();

        var runner = new DepotDownloaderRunner();
        Console.WriteLine($"  tool dir: {runner.ToolDirectory}");

        var log = new Progress<string>(message => Console.WriteLine($"  .. {message}"));
        var acquired = await runner.EnsureToolAsync(log);

        if (!acquired || !runner.IsInstalled)
        {
            Console.WriteLine("TOOL CHECK FAILED: could not acquire DepotDownloader");
            return 1;
        }

        var size = new FileInfo(runner.ToolPath).Length / (1024.0 * 1024.0);
        Console.WriteLine($"  tool: {runner.ToolPath} ({size:F1} MB)");
        Console.WriteLine();

        // --version is the only harmless invocation: -help is not a flag and
        // exits with "-app not specified".
        var psi = new ProcessStartInfo(runner.ToolPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--version");

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        Console.WriteLine($"  --version exit={process.ExitCode}");
        foreach (var line in (stdout + stderr)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Take(4))
            Console.WriteLine($"    {line}");

        if (process.ExitCode != 0 ||
            !stdout.Contains("DepotDownloader", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("TOOL CHECK FAILED: the binary did not start");
            return 1;
        }

        Console.WriteLine("TOOL CHECK OK");
        return 0;
    }
}
