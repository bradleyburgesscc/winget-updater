namespace WingetUpdater;

/// <summary>
/// One row parsed from `winget upgrade`.
/// </summary>
public sealed record PackageUpgrade(
    string Name,
    string Id,
    string CurrentVersion,
    string AvailableVersion,
    string Source)
{
    /// <summary>True when winget could not determine the installed version.</summary>
    public bool CurrentVersionUnknown =>
        string.Equals(CurrentVersion, "Unknown", StringComparison.OrdinalIgnoreCase);
}
