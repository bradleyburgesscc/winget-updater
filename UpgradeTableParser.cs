namespace WingetUpdater;

/// <summary>
/// Parses the fixed-width text table printed by `winget upgrade`.
///
/// winget has no structured (JSON) output for this command, so the table must be sliced
/// by column position. Column start offsets are derived from the header row rather than
/// matched by header text, because header text is localized but column position is not.
/// Data rows are sliced by *display column*, not character index, because a wide (e.g.
/// CJK) character in an earlier column shifts every later column by two display cells
/// while only consuming one UTF-16 char.
/// </summary>
public static class UpgradeTableParser
{
    public sealed record ParseResult(IReadOnlyList<PackageUpgrade> Upgrades, int AdditionalExplicitTargetingCount);

    private static readonly ParseResult Empty = new(Array.Empty<PackageUpgrade>(), 0);

    public static ParseResult Parse(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            return Empty;
        }

        var lines = stdout.Replace("\r\n", "\n").Split('\n');

        int sepIndex = FindSeparatorLine(lines, 0);
        if (sepIndex <= 0)
        {
            // No table at all -- e.g. "No installed package found matching input criteria."
            // This is the normal "nothing to upgrade" shape, not an error.
            return Empty;
        }

        var starts = GetColumnStarts(lines[sepIndex - 1]);
        if (starts.Count is < 4 or > 5)
        {
            // Unrecognized table shape. Fail soft rather than throw on an unexpected
            // winget output format (e.g. a future/older CLI version).
            return Empty;
        }

        var upgrades = new List<PackageUpgrade>();
        int i = sepIndex + 1;
        for (; i < lines.Length; i++)
        {
            string line = lines[i];

            string id = SliceByDisplayColumns(line, starts[1], starts[2]);
            if (id.Length == 0)
            {
                // Blank line, "N upgrades available." summary, or any other non-data line.
                break;
            }

            string name = SliceByDisplayColumns(line, starts[0], starts[1]);
            string version = SliceByDisplayColumns(line, starts[2], starts[3]);
            string available = starts.Count >= 5
                ? SliceByDisplayColumns(line, starts[3], starts[4])
                : SliceByDisplayColumns(line, starts[3], null);
            string source = starts.Count >= 5
                ? SliceByDisplayColumns(line, starts[4], null)
                : "";

            if (available.Length == 0)
            {
                // winget occasionally lists a package with no resolvable "Available"
                // version. It can't be driven by --version, so it isn't upgradeable here.
                continue;
            }

            upgrades.Add(new PackageUpgrade(name, id, version, available, source));
        }

        int additional = CountSecondTable(lines, i);
        return new ParseResult(upgrades, additional);
    }

    /// <summary>
    /// winget sometimes prints a second table headed by a note that those packages
    /// "require explicit targeting for upgrade" (version-pinned / has-multiple-installers
    /// cases). Those rows can't be driven by a plain `--id`/`--version` upgrade, so they
    /// are only counted for a footer note, not parsed into PackageUpgrade rows.
    /// </summary>
    private static int CountSecondTable(string[] lines, int fromIndex)
    {
        int sepIndex = FindSeparatorLine(lines, fromIndex);
        if (sepIndex <= fromIndex)
        {
            return 0;
        }

        int count = 0;
        for (int i = sepIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static int FindSeparatorLine(string[] lines, int fromIndex)
    {
        for (int i = fromIndex; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length >= 10 && IsAllDashes(line))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAllDashes(string line)
    {
        foreach (char c in line)
        {
            if (c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Column start offsets, taken from where each header word begins. Header text
    /// (Name/Id/Version/Available/Source, or localized equivalents) is always plain
    /// single-word ASCII with no internal spaces, so a space -&gt; non-space transition
    /// reliably marks a column boundary regardless of locale.
    /// </summary>
    private static List<int> GetColumnStarts(string header)
    {
        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    /// <summary>
    /// Extracts the text spanning display columns [startCol, endCol), or [startCol, end
    /// of line) when endCol is null. Walks the string accumulating display width so that
    /// wide characters earlier in the line don't throw off later column boundaries.
    /// Returns "" if the line is shorter than startCol.
    /// </summary>
    internal static string SliceByDisplayColumns(string line, int startCol, int? endCol)
    {
        int col = 0;
        int startIndex = -1;
        int endIndex = line.Length;
        int i = 0;

        while (i < line.Length)
        {
            if (startIndex == -1 && col >= startCol)
            {
                startIndex = i;
            }

            if (endCol.HasValue && col >= endCol.Value)
            {
                endIndex = i;
                break;
            }

            int cp;
            int charLen;
            if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
            {
                cp = char.ConvertToUtf32(line[i], line[i + 1]);
                charLen = 2;
            }
            else
            {
                cp = line[i];
                charLen = 1;
            }

            col += CharDisplayWidth(cp);
            i += charLen;
        }

        if (startIndex == -1)
        {
            return "";
        }

        if (!endCol.HasValue)
        {
            endIndex = line.Length;
        }

        if (endIndex < startIndex)
        {
            endIndex = startIndex;
        }

        return line.Substring(startIndex, endIndex - startIndex).Trim();
    }

    /// <summary>
    /// Sum of per-character display widths, used by the self-test fixture builder to
    /// pad synthetic table rows the same way winget pads real ones (by display cell,
    /// not UTF-16 char count).
    /// </summary>
    internal static int MeasureDisplayWidth(string s)
    {
        int total = 0;
        int i = 0;
        while (i < s.Length)
        {
            int cp;
            int charLen;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                charLen = 2;
            }
            else
            {
                cp = s[i];
                charLen = 1;
            }

            total += CharDisplayWidth(cp);
            i += charLen;
        }

        return total;
    }

    /// <summary>
    /// Approximate East-Asian-Width-aware display width: 2 for wide/fullwidth ranges
    /// (CJK, Hangul, fullwidth forms, most emoji), 0 for combining marks and other
    /// zero-width format characters, 1 otherwise.
    /// </summary>
    private static int CharDisplayWidth(int cp)
    {
        if (cp == 0)
        {
            return 0;
        }

        if (cp == 0x00AD
            || (cp >= 0x0300 && cp <= 0x036F)   // combining diacritical marks
            || (cp >= 0x200B && cp <= 0x200F)   // zero-width space/joiners/marks
            || (cp >= 0xFE00 && cp <= 0xFE0F))  // variation selectors
        {
            return 0;
        }

        bool isWide =
            (cp >= 0x1100 && cp <= 0x115F) ||   // Hangul Jamo
            (cp >= 0x2E80 && cp <= 0x303E) ||   // CJK radicals, punctuation
            (cp >= 0x3041 && cp <= 0x33FF) ||   // Hiragana..CJK compat
            (cp >= 0x3400 && cp <= 0x4DBF) ||   // CJK ext A
            (cp >= 0x4E00 && cp <= 0x9FFF) ||   // CJK unified ideographs
            (cp >= 0xA000 && cp <= 0xA4CF) ||   // Yi
            (cp >= 0xAC00 && cp <= 0xD7A3) ||   // Hangul syllables
            (cp >= 0xF900 && cp <= 0xFAFF) ||   // CJK compat ideographs
            (cp >= 0xFF00 && cp <= 0xFF60) ||   // fullwidth forms
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||   // fullwidth signs
            (cp >= 0x1F300 && cp <= 0x1FAFF) || // emoji / symbols
            (cp >= 0x20000 && cp <= 0x3FFFD);   // CJK ext B+

        return isWide ? 2 : 1;
    }
}
