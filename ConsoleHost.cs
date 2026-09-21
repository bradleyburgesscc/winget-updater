using System.Runtime.InteropServices;

namespace WingetUpdater;

/// <summary>
/// Puts the console into alt-screen / VT mode for the TUI and guarantees it is restored
/// afterwards, including on Ctrl+C, so a crash or quit never leaves the terminal in a
/// broken state (no cursor, stuck alt screen, raw mode left on, etc.).
/// </summary>
public sealed class ConsoleHost : IDisposable
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    private readonly nint _stdOutHandle;
    private readonly uint _originalMode;
    private readonly bool _vtSupported;
    private bool _disposed;

    public bool VirtualTerminalEnabled => _vtSupported;

    public ConsoleHost()
    {
        _stdOutHandle = GetStdHandle(STD_OUTPUT_HANDLE);

        if (_stdOutHandle != nint.Zero && GetConsoleMode(_stdOutHandle, out _originalMode))
        {
            uint newMode = _originalMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING
                                          | ENABLE_PROCESSED_OUTPUT
                                          | ENABLE_WRAP_AT_EOL_OUTPUT;
            _vtSupported = SetConsoleMode(_stdOutHandle, newMode);
        }
        else
        {
            _originalMode = 0;
            _vtSupported = false;
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        if (_vtSupported)
        {
            Console.Out.Write("\x1b[?1049h"); // enter alt screen
            Console.Out.Write("\x1b[?25l");   // hide cursor
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Let the process exit normally, but restore the terminal first so Ctrl+C
        // never leaves the user on a blank alt-screen with no cursor.
        Restore();
    }

    private void Restore()
    {
        if (_vtSupported)
        {
            Console.Out.Write("\x1b[?25h");   // show cursor
            Console.Out.Write("\x1b[?1049l"); // leave alt screen
            Console.Out.Flush();
        }

        if (_stdOutHandle != nint.Zero && _originalMode != 0)
        {
            SetConsoleMode(_stdOutHandle, _originalMode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;
        Restore();
    }
}
