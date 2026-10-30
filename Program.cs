using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WingetUpdater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
        {
            PrintHelp();
            return 0;
        }

        if (args.Contains("--selftest"))
        {
            return SelfTest.Run() ? 0 : 1;
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("winget-updater is an interactive TUI and needs a real console — it can't run with input or output redirected/piped.");
            Console.Error.WriteLine("Run it directly in a terminal (e.g. Windows Terminal, PowerShell, or cmd), or use --selftest for a non-interactive check.");
            return 1;
        }

        bool noElevate = args.Contains("--no-elevate");
        bool relaunched = args.Contains("--relaunched");
        bool includePinned = args.Contains("--include-pinned");
        bool includeUnknown = args.Contains("--include-unknown");

        if (!noElevate && !IsElevated())
        {
            if (TryRelaunchElevated(args))
            {
                return 0;
            }

            Console.WriteLine("Continuing without administrator privileges — machine-scoped upgrades may each prompt for elevation individually.");
            Console.WriteLine();
        }

        int exitCode;
        try
        {
            exitCode = await RunAsync(includeUnknown, includePinned);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            exitCode = 1;
        }

        if (relaunched)
        {
            // This process is running in its own elevated console window (spawned via
            // "runas"). Without this, the window -- and the upgrade summary in it --
            // would disappear the instant the process exits.
            Console.WriteLine();
            Console.WriteLine("Press any key to close…");
            Console.ReadKey(intercept: true);
        }

        return exitCode;
    }

    private static async Task<int> RunAsync(bool includeUnknown, bool includePinned)
    {
        var host = new ConsoleHost();
        UpgradeTableParser.ParseResult initial;

        try
        {
            Console.Out.Write("Querying winget for available upgrades…\n");
            Console.Out.Flush();
            initial = await WingetClient.QueryUpgradesAsync(includeUnknown, includePinned, CancellationToken.None);
        }
        catch (Exception ex)
        {
            host.Dispose();
            Console.Error.WriteLine($"Failed to query winget: {ex.Message}");
            return 1;
        }

        var tui = new Tui(host, initial, includeUnknown, includePinned, WingetClient.QueryUpgradesAsync);
        Tui.Result result;
        try
        {
            result = await tui.RunAsync(CancellationToken.None);
        }
        finally
        {
            // Always leave the alt screen / restore the console before doing anything
            // else, whether the user quit, picked packages, or an exception unwound here.
            host.Dispose();
        }

        if (result.Action == Tui.ExitAction.Quit || result.Selected.Count == 0)
        {
            return 0;
        }

        return await RunUpgradesAsync(result.Selected);
    }

    private static async Task<int> RunUpgradesAsync(IReadOnlyList<PackageUpgrade> selected)
    {
        Console.WriteLine();
        Console.WriteLine($"Upgrading {selected.Count} package(s)...");
        Console.WriteLine();

        var runResults = new List<UpgradeRunResult>();
        foreach (var pkg in selected)
        {
            Console.WriteLine($"--- {pkg.Id}  ({pkg.CurrentVersion} -> {pkg.AvailableVersion}) ---");
            var runResult = await WingetClient.RunUpgradeAsync(pkg, CancellationToken.None);
            runResults.Add(runResult);
            Console.WriteLine();
        }

        PrintSummary(runResults);

        return runResults.Any(r => r.ExitCode != 0) ? 1 : 0;
    }

    private static void PrintSummary(IReadOnlyList<UpgradeRunResult> results)
    {
        Console.WriteLine("Summary");
        Console.WriteLine("-------");
        foreach (var r in results)
        {
            Console.WriteLine($"  {r.Package.Id,-45} {r.StatusText}");
        }

        int succeeded = results.Count(r => r.ExitCode == 0);
        Console.WriteLine();
        Console.WriteLine($"{succeeded} of {results.Count} package(s) upgraded successfully.");
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches this executable with a UAC elevation prompt, forwarding the original
    /// arguments plus --relaunched. Returns true if the elevated process was started
    /// (the caller should then simply exit), or false if the user cancelled the UAC
    /// prompt or the relaunch otherwise failed, in which case the caller continues
    /// unelevated rather than treating this as fatal.
    /// </summary>
    private static bool TryRelaunchElevated(string[] originalArgs)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
            };

            foreach (var a in originalArgs)
            {
                psi.ArgumentList.Add(a);
            }
            psi.ArgumentList.Add("--relaunched");

            using var process = Process.Start(psi);
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user clicked "No" on the UAC prompt.
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not relaunch elevated: {ex.Message}");
            return false;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            winget-updater — interactive picker for `winget upgrade`

            Usage: winget-updater [options]

              --no-elevate           Don't relaunch elevated; run with current privileges.
              --include-pinned       Also list packages that have a non-blocking pin.
              --include-unknown      Also list packages whose installed version is unknown.
              --selftest             Run internal parser self-tests and exit.
              -h, --help             Show this help.

            Controls: ↑/↓ move, space select, a select all, n select none,
                      u toggle "include unknown", p toggle "include pinned",
                      r refresh, enter upgrade selected, q/Esc quit.
            """);
    }
}
