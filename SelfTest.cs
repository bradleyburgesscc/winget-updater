using System.Text;

namespace WingetUpdater;

/// <summary>
/// Fixture-based tests for <see cref="UpgradeTableParser"/>, run via the hidden
/// `--selftest` flag instead of a separate test project so the tool stays a single exe.
/// </summary>
public static class SelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Passed, string? Detail)>
        {
            RunCase("basic 4-column table with Unknown version", BasicTable),
            RunCase("Id and Version containing spaces", SpacesInId),
            RunCase("empty Available column is skipped, later rows still parsed", EmptyAvailable),
            RunCase("5-column table with Source", WithSource),
            RunCase("wide (CJK) package name keeps later columns aligned", WideName),
            RunCase("no upgrades / exit-255 shape produces an empty result", NoUpgrades),
            RunCase("second 'explicit targeting' table is counted, not parsed", SecondTable),
        };

        Console.WriteLine();
        Console.WriteLine("winget-updater self-test");
        Console.WriteLine("=========================");
        foreach (var (name, passed, detail) in results)
        {
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}");
            if (!passed && detail is not null)
            {
                Console.WriteLine($"       {detail}");
            }
        }

        int failed = results.Count(r => !r.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {results.Count} checks passed."
            : $"{failed} of {results.Count} checks FAILED.");

        return failed == 0;
    }

    private static (string, bool, string?) RunCase(string name, Func<string?> testFunc)
    {
        try
        {
            string? failure = testFunc();
            return (name, failure is null, failure);
        }
        catch (Exception ex)
        {
            return (name, false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- fixtures -----------------------------------------------------------------

    private static string? BasicTable()
    {
        string table = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "Docker Desktop", "Docker.DockerDesktop", "4.48.0", "4.51.2" },
                new[] { "Microsoft SQL Server 2025 (64-bit)", "Microsoft.SQLServer.2025.Developer", "Unknown", "17.0.1000.7" },
            },
            footer: "2 upgrades available.");

        var result = UpgradeTableParser.Parse(table);

        if (result.Upgrades.Count != 2)
        {
            return $"expected 2 upgrades, got {result.Upgrades.Count}";
        }

        var docker = result.Upgrades[0];
        if (docker.Id != "Docker.DockerDesktop" || docker.CurrentVersion != "4.48.0" || docker.AvailableVersion != "4.51.2")
        {
            return $"docker row parsed wrong: Id='{docker.Id}' Current='{docker.CurrentVersion}' Available='{docker.AvailableVersion}'";
        }

        var sql = result.Upgrades[1];
        if (!sql.CurrentVersionUnknown || sql.AvailableVersion != "17.0.1000.7")
        {
            return $"sql row parsed wrong: Current='{sql.CurrentVersion}' Available='{sql.AvailableVersion}'";
        }

        return null;
    }

    private static string? SpacesInId()
    {
        const string id = "ARP\\User\\X64\\JetBrains Toolbox (AndroidStudio) 19d57668-a294-4f05-a2e4-15d7cb57bfa1";
        string table = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "Android Studio", id, "Quail 3 2026.1.2", "Quail 3 2026.1.3" },
            },
            footer: "1 upgrades available.");

        var result = UpgradeTableParser.Parse(table);

        if (result.Upgrades.Count != 1)
        {
            return $"expected 1 upgrade, got {result.Upgrades.Count}";
        }

        var row = result.Upgrades[0];
        if (row.Id != id)
        {
            return $"Id with embedded spaces was mis-sliced: got '{row.Id}'";
        }

        if (row.CurrentVersion != "Quail 3 2026.1.2" || row.AvailableVersion != "Quail 3 2026.1.3")
        {
            return $"version columns with embedded spaces were mis-sliced: Current='{row.CurrentVersion}' Available='{row.AvailableVersion}'";
        }

        return null;
    }

    private static string? EmptyAvailable()
    {
        string table = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "Something Unresolvable", "Vendor.Something", "1.0.0", "" },
                new[] { "Git", "Git.Git", "2.51.0", "2.52.1" },
            },
            footer: "1 upgrades available.");

        var result = UpgradeTableParser.Parse(table);

        if (result.Upgrades.Count != 1)
        {
            return $"expected the empty-Available row to be skipped and 1 row kept, got {result.Upgrades.Count}";
        }

        if (result.Upgrades[0].Id != "Git.Git")
        {
            return $"expected the surviving row to be Git.Git, got '{result.Upgrades[0].Id}'";
        }

        return null;
    }

    private static string? WithSource()
    {
        string table = BuildTable(
            new[] { "Name", "Id", "Version", "Available", "Source" },
            new[]
            {
                new[] { "7-Zip 25.00 (x64 edition)", "7zip.7zip", "25.00.00.0", "26.03.00.0", "winget" },
            },
            footer: "1 upgrades available.");

        var result = UpgradeTableParser.Parse(table);

        if (result.Upgrades.Count != 1)
        {
            return $"expected 1 upgrade, got {result.Upgrades.Count}";
        }

        var row = result.Upgrades[0];
        if (row.Source != "winget")
        {
            return $"expected Source='winget', got '{row.Source}'";
        }

        if (row.Id != "7zip.7zip" || row.AvailableVersion != "26.03.00.0")
        {
            return $"5-column row parsed wrong: Id='{row.Id}' Available='{row.AvailableVersion}'";
        }

        return null;
    }

    private static string? WideName()
    {
        string table = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "日本語アプリケーション", "Vendor.JapaneseApp", "1.0", "2.0" },
                new[] { "Git", "Git.Git", "2.51.0", "2.52.1" },
            },
            footer: "2 upgrades available.");

        var result = UpgradeTableParser.Parse(table);

        if (result.Upgrades.Count != 2)
        {
            return $"expected 2 upgrades, got {result.Upgrades.Count}";
        }

        var wide = result.Upgrades[0];
        if (wide.Id != "Vendor.JapaneseApp" || wide.CurrentVersion != "1.0" || wide.AvailableVersion != "2.0")
        {
            return $"row after a wide (CJK) name was mis-sliced: Id='{wide.Id}' Current='{wide.CurrentVersion}' Available='{wide.AvailableVersion}'";
        }

        var git = result.Upgrades[1];
        if (git.Id != "Git.Git" || git.AvailableVersion != "2.52.1")
        {
            return $"row after the CJK row was mis-sliced: Id='{git.Id}' Available='{git.AvailableVersion}'";
        }

        return null;
    }

    private static string? NoUpgrades()
    {
        const string output =
            "No installed package found matching input criteria.\n" +
            "1 package(s) have version numbers that cannot be determined. Use --include-unknown to see all results.\n";

        var result = UpgradeTableParser.Parse(output);

        if (result.Upgrades.Count != 0)
        {
            return $"expected 0 upgrades for the exit-255 / no-match shape, got {result.Upgrades.Count}";
        }

        if (result.AdditionalExplicitTargetingCount != 0)
        {
            return $"expected 0 additional-explicit count, got {result.AdditionalExplicitTargetingCount}";
        }

        return null;
    }

    private static string? SecondTable()
    {
        string firstTable = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "Docker Desktop", "Docker.DockerDesktop", "4.48.0", "4.51.2" },
            },
            footer: "1 upgrades available.");

        string secondTable = BuildTable(
            new[] { "Name", "Id", "Version", "Available" },
            new[]
            {
                new[] { "Pinned App One", "Vendor.PinnedOne", "1.0", "1.1" },
                new[] { "Pinned App Two", "Vendor.PinnedTwo", "2.0", "2.1" },
                new[] { "Pinned App Three", "Vendor.PinnedThree", "3.0", "3.1" },
            },
            footer: null);

        string combined = firstTable +
            "\n" +
            "The following packages have version numbers that require explicit targeting for upgrade:\n" +
            secondTable;

        var result = UpgradeTableParser.Parse(combined);

        if (result.Upgrades.Count != 1 || result.Upgrades[0].Id != "Docker.DockerDesktop")
        {
            return $"expected only the first table's 1 row parsed, got {result.Upgrades.Count}";
        }

        if (result.AdditionalExplicitTargetingCount != 3)
        {
            return $"expected 3 additional explicit-targeting rows counted, got {result.AdditionalExplicitTargetingCount}";
        }

        return null;
    }

    // ---- fixture builder ------------------------------------------------------------

    /// <summary>
    /// Builds a synthetic table in the same shape winget prints: each column padded
    /// (by display width, matching real winget) to the widest header/value in that
    /// column, plus a one-cell gap, with the final column left unpadded.
    /// </summary>
    private static string BuildTable(string[] headers, string[][] rows, string? footer)
    {
        int columns = headers.Length;
        var widths = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            widths[c] = UpgradeTableParser.MeasureDisplayWidth(headers[c]);
            foreach (var row in rows)
            {
                widths[c] = Math.Max(widths[c], UpgradeTableParser.MeasureDisplayWidth(row[c]));
            }

            widths[c] += 1; // gap before the next column
        }

        var sb = new StringBuilder();
        AppendLine(sb, headers, widths, columns);

        int totalWidth = widths.Sum() + 2;
        sb.Append(new string('-', totalWidth)).Append('\n');

        foreach (var row in rows)
        {
            AppendLine(sb, row, widths, columns);
        }

        if (footer is not null)
        {
            sb.Append(footer).Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string[] cols, int[] widths, int columns)
    {
        for (int c = 0; c < columns; c++)
        {
            bool isLast = c == columns - 1;
            if (isLast)
            {
                sb.Append(cols[c]);
            }
            else
            {
                sb.Append(PadDisplay(cols[c], widths[c]));
            }
        }

        sb.Append('\n');
    }

    private static string PadDisplay(string s, int width)
    {
        int pad = width - UpgradeTableParser.MeasureDisplayWidth(s);
        return pad > 0 ? s + new string(' ', pad) : s;
    }
}
