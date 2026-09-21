using System.Text;

namespace WingetUpdater;

public sealed class Tui
{
    public enum ExitAction { Quit, Upgrade }

    public sealed record Result(ExitAction Action, IReadOnlyList<PackageUpgrade> Selected);

    private readonly ConsoleHost _host;
    private readonly Func<bool, bool, CancellationToken, Task<UpgradeTableParser.ParseResult>> _requery;

    private List<PackageUpgrade> _items;
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private int _cursor;
    private int _scrollOffset;
    private bool _includeUnknown;
    private bool _includePinned;
    private int _additionalExplicit;
    private string? _statusMessage;

    public Tui(
        ConsoleHost host,
        UpgradeTableParser.ParseResult initial,
        bool includeUnknown,
        bool includePinned,
        Func<bool, bool, CancellationToken, Task<UpgradeTableParser.ParseResult>> requery)
    {
        _host = host;
        _items = initial.Upgrades.ToList();
        _additionalExplicit = initial.AdditionalExplicitTargetingCount;
        _includeUnknown = includeUnknown;
        _includePinned = includePinned;
        _requery = requery;
    }

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        Render();

        while (!ct.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(15, ct);
                continue;
            }

            var key = Console.ReadKey(intercept: true);
            bool clearStatus = true;

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    MoveCursor(-1);
                    break;

                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    MoveCursor(1);
                    break;

                case ConsoleKey.PageUp:
                    MoveCursor(-Math.Max(1, ViewportHeight()));
                    break;

                case ConsoleKey.PageDown:
                    MoveCursor(Math.Max(1, ViewportHeight()));
                    break;

                case ConsoleKey.Home:
                    MoveCursorTo(0);
                    break;

                case ConsoleKey.End:
                    MoveCursorTo(_items.Count - 1);
                    break;

                case ConsoleKey.Spacebar:
                    ToggleCurrent();
                    break;

                case ConsoleKey.A:
                    foreach (var item in _items)
                    {
                        _selectedIds.Add(item.Id);
                    }
                    break;

                case ConsoleKey.N:
                    _selectedIds.Clear();
                    break;

                case ConsoleKey.U:
                    _includeUnknown = !_includeUnknown;
                    await RequeryAsync(ct);
                    clearStatus = false;
                    break;

                case ConsoleKey.P:
                    _includePinned = !_includePinned;
                    await RequeryAsync(ct);
                    clearStatus = false;
                    break;

                case ConsoleKey.R:
                    await RequeryAsync(ct);
                    clearStatus = false;
                    break;

                case ConsoleKey.Enter:
                    if (_selectedIds.Count == 0)
                    {
                        _statusMessage = "Nothing selected — press space to select a package first.";
                        clearStatus = false;
                    }
                    else
                    {
                        return new Result(ExitAction.Upgrade, _items.Where(i => _selectedIds.Contains(i.Id)).ToList());
                    }
                    break;

                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return new Result(ExitAction.Quit, Array.Empty<PackageUpgrade>());
            }

            if (clearStatus)
            {
                _statusMessage = null;
            }

            Render();
        }

        return new Result(ExitAction.Quit, Array.Empty<PackageUpgrade>());
    }

    private async Task RequeryAsync(CancellationToken ct)
    {
        _statusMessage = "Querying winget…";
        Render();

        var result = await _requery(_includeUnknown, _includePinned, ct);

        _items = result.Upgrades.ToList();
        _additionalExplicit = result.AdditionalExplicitTargetingCount;
        _selectedIds.RemoveWhere(id => !_items.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)));
        _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _items.Count - 1));
        _scrollOffset = 0;
        _statusMessage = null;
    }

    private void MoveCursor(int delta) => MoveCursorTo(_cursor + delta);

    private void MoveCursorTo(int index)
    {
        if (_items.Count == 0)
        {
            _cursor = 0;
            return;
        }

        _cursor = Math.Clamp(index, 0, _items.Count - 1);

        int viewport = ViewportHeight();
        if (_cursor < _scrollOffset)
        {
            _scrollOffset = _cursor;
        }
        else if (_cursor >= _scrollOffset + viewport)
        {
            _scrollOffset = _cursor - viewport + 1;
        }
    }

    private void ToggleCurrent()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var id = _items[_cursor].Id;
        if (!_selectedIds.Remove(id))
        {
            _selectedIds.Add(id);
        }
    }

    private static int ViewportHeight()
    {
        int height = SafeWindowHeight();
        // 2 header lines + 1 blank + list + 1 blank + 2 footer lines
        return Math.Max(3, height - 6);
    }

    private static int SafeWindowHeight()
    {
        try
        {
            return Console.WindowHeight > 0 ? Console.WindowHeight : 25;
        }
        catch
        {
            return 25;
        }
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch
        {
            return 80;
        }
    }

    private void Render()
    {
        int width = SafeWindowWidth();
        int viewport = ViewportHeight();

        // Keep the viewport in sync in case the terminal was resized since the last frame.
        if (_cursor < _scrollOffset)
        {
            _scrollOffset = _cursor;
        }
        else if (_cursor >= _scrollOffset + viewport)
        {
            _scrollOffset = Math.Max(0, _cursor - viewport + 1);
        }

        var sb = new StringBuilder();

        sb.Append("  winget upgrade — ");
        sb.Append(_selectedIds.Count);
        sb.Append(" of ");
        sb.Append(_items.Count);
        sb.Append(" selected");
        sb.Append("    [unknown: ").Append(_includeUnknown ? "on " : "off").Append("  pinned: ").Append(_includePinned ? "on " : "off").Append(']');
        sb.Append('\n');

        if (_additionalExplicit > 0)
        {
            sb.Append("  (").Append(_additionalExplicit)
              .Append(_additionalExplicit == 1 ? " package requires" : " packages require")
              .Append(" explicit targeting and is not listed here)\n");
        }
        else
        {
            sb.Append('\n');
        }
        sb.Append('\n');

        if (_items.Count == 0)
        {
            sb.Append("  No upgrades available.\n");
            int blankRows = Math.Max(0, viewport - 1);
            for (int i = 0; i < blankRows; i++)
            {
                sb.Append('\n');
            }
        }
        else
        {
            RenderRows(sb, width, viewport);
        }

        sb.Append('\n');
        sb.Append("  ↑/↓ move   space select   a all   n none   u unknown   p pinned   r refresh\n");
        sb.Append("  enter upgrade   q quit");

        if (_statusMessage is not null)
        {
            sb.Append("\n\n  ").Append(_statusMessage);
        }

        WriteFrame(sb.ToString());
    }

    private void RenderRows(StringBuilder sb, int width, int viewport)
    {
        int idWidth = Math.Clamp(width - 42, 16, 44);
        const int verWidth = 12;

        int end = Math.Min(_items.Count, _scrollOffset + viewport);
        for (int i = _scrollOffset; i < end; i++)
        {
            var item = _items[i];
            bool isCursor = i == _cursor;
            bool isSelected = _selectedIds.Contains(item.Id);

            sb.Append(isCursor ? "> " : "  ");
            sb.Append('[').Append(isSelected ? 'x' : ' ').Append("] ");
            sb.Append(Pad(item.Id, idWidth)).Append(' ');
            sb.Append(Pad(item.CurrentVersion, verWidth));
            sb.Append(" -> ");
            sb.Append(Pad(item.AvailableVersion, verWidth));

            // Only show the friendly name if there's meaningful room left; it's a
            // secondary column, the Id is the primary identifier for this tool.
            int used = 2 + 4 + idWidth + 1 + verWidth + 4 + verWidth + 2;
            int remaining = width - used;
            if (remaining > 8 && !string.IsNullOrEmpty(item.Name))
            {
                sb.Append("  ").Append(Truncate(item.Name, remaining - 2));
            }

            sb.Append('\n');
        }

        int shownRows = end - _scrollOffset;
        for (int i = shownRows; i < viewport; i++)
        {
            sb.Append('\n');
        }
    }

    private static string Pad(string s, int width)
    {
        string t = Truncate(s, width);
        return t.PadRight(width);
    }

    private static string Truncate(string s, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        if (s.Length <= width)
        {
            return s;
        }

        if (width == 1)
        {
            return "…";
        }

        return s.Substring(0, width - 1) + "…";
    }

    private void WriteFrame(string frame)
    {
        if (_host.VirtualTerminalEnabled)
        {
            Console.Out.Write("\x1b[H\x1b[J");
        }
        else
        {
            Console.Clear();
        }

        Console.Out.Write(frame);
        Console.Out.Flush();
    }
}
