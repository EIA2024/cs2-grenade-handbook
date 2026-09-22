using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Handbook.Desktop;

// All native window arguments are obtained from OUR WPF WindowInteropHelper only.
// No external window lookup, process handles, hooks, input synthesis or foreground APIs.
internal static class NativeWindow
{
    private const int GwlExStyle = -20;
    internal const long Transparent = 0x20, Layered = 0x80000, NoActivate = 0x8000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public static long ReadOwnStyle(Window window)
    {
        Marshal.SetLastPInvokeError(0);
        var value = GetStyle(new WindowInteropHelper(window).Handle, GwlExStyle);
        if (value == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return value.ToInt64();
    }
    public static void SetPassThrough(Window window, bool passThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var style = ReadOwnStyle(window);
        // Layered + transparent makes hit-testing pass through to windows in OTHER processes too.
        style = passThrough ? style | Transparent | Layered | NoActivate : style & ~(Transparent | NoActivate);
        Marshal.SetLastPInvokeError(0);
        if (SetStyle(handle, GwlExStyle, (nint)style) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        if (!SetWindowPos(handle, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
}

