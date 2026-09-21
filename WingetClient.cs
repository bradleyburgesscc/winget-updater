using System.Diagnostics;
using System.Text;

namespace WingetUpdater;

public sealed record UpgradeRunResult(PackageUpgrade Package, int ExitCode, string StatusText);

public static class WingetClient
{
    /// <summary>
    /// Runs `winget upgrade` (list mode, nothing is installed) and parses the result.
    /// winget exits 255 with "No installed package found matching input criteria." when
    /// there is nothing to upgrade -- that is a normal outcome here, not a failure, so
    /// the exit code is intentionally not treated as a success/failure signal.
    /// </summary>
    public static async Task<UpgradeTableParser.ParseResult> QueryUpgradesAsync(
        bool includeUnknown, bool includePinned, CancellationToken ct)
    {
        var args = new List<string>
        {
            "upgrade",
            "--disable-interactivity",
            "--accept-source-agreements",
        };

        if (includeUnknown)
        {
            args.Add("--include-unknown");
        }

        if (includePinned)
        {
            args.Add("--include-pinned");
        }

        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start winget. Is it installed and on PATH?");
        }

        process.BeginOutputReadLine();
        // Standard error is redirected (so it doesn't bleed into our console) but
        // intentionally not read -- winget's warnings there aren't needed for parsing.
        string stderrText = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return UpgradeTableParser.Parse(stdout.ToString());
    }

    /// <summary>
    /// Runs `winget upgrade --id ...` for one package with no I/O redirection, so the
    /// child inherits the console and winget's native progress bars / prompts render
    /// as normal. Deliberately omits --disable-interactivity: the user is watching this
    /// one and an installer may legitimately need input.
    /// </summary>
    public static async Task<UpgradeRunResult> RunUpgradeAsync(PackageUpgrade package, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        psi.ArgumentList.Add("upgrade");
        psi.ArgumentList.Add("--id");
        psi.ArgumentList.Add(package.Id);
        psi.ArgumentList.Add("--exact");
        psi.ArgumentList.Add("--version");
        psi.ArgumentList.Add(package.AvailableVersion);
        psi.ArgumentList.Add("--accept-package-agreements");
        psi.ArgumentList.Add("--accept-source-agreements");

        if (package.CurrentVersionUnknown)
        {
            psi.ArgumentList.Add("--include-unknown");
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(ct);

        return new UpgradeRunResult(package, process.ExitCode, DescribeExitCode(process.ExitCode));
    }

    private static string DescribeExitCode(int exitCode) => exitCode switch
    {
        0 => "Success",
        unchecked((int)0x8A15002B) => "No applicable upgrade found",
        unchecked((int)0x8A150109) => "Reboot required to finish",
        unchecked((int)0x8A150056) => "Application currently running (close it and retry)",
        _ => $"Failed (0x{exitCode:X8})",
    };
}
