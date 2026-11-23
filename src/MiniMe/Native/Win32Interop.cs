using System.Runtime.InteropServices;
using System.Text;

namespace MiniMe.Native;

public static class Win32Interop
{
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_H = 0x48;
    public const uint VK_S = 0x53;
    public const int VK_ESCAPE = 0x1B;
    public const int WM_HOTKEY = 0x0312;

    public const int BOSS_MODE_HOTKEY_ID = 0x4D4D;
    public const int SCREEN_SCAN_HOTKEY_ID = 0x4D53;

    private static readonly HashSet<string> ProtectedShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow",
        "TaskListThumbnailWnd",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "Shell_InputSwitchTopLevelWindow"
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, RECT Rect, uint ProcessId, bool IsMaximized);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(
        IntPtr hWnd,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    public static bool IsEscapePressed() => (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;

    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        int copied = GetClassName(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    public static string GetActiveWindowTitle() => GetWindowTitle(GetForegroundWindow());

    public static bool IsOwnProcessWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hWnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static bool IsWindowSafeToInteract(
        IntPtr hWnd,
        int screenPixelWidth,
        int screenPixelHeight,
        bool allowMaximizedApp,
        out WindowInfo? info)
    {
        info = null;
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        string className = GetWindowClassName(hWnd);
        if (string.IsNullOrWhiteSpace(className) || ProtectedShellClasses.Contains(className))
        {
            return false;
        }

        int exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt32();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        string title = GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out RECT rect))
        {
            return false;
        }

        if (rect.Width < 240 || rect.Height < 150)
        {
            return false;
        }

        bool isZoomed = IsZoomed(hWnd);
        int style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt32();
        bool hasCaption = (style & WS_CAPTION) != 0;

        // Always reject borderless fullscreen games / video players
        bool isBorderlessFullscreen =
            !isZoomed &&
            !hasCaption &&
            rect.Width >= screenPixelWidth - 4 &&
            rect.Height >= screenPixelHeight - 4;
        if (isBorderlessFullscreen)
        {
            return false;
        }

        if (isZoomed && !allowMaximizedApp)
        {
            return false;
        }

        // Guard against completely off-screen windows
        if (rect.Right <= 60 || rect.Left >= screenPixelWidth - 60 || rect.Bottom <= 60 || rect.Top >= screenPixelHeight - 60)
        {
            return false;
        }

        info = new WindowInfo(hWnd, title, className, rect, pid, isZoomed);
        return true;
    }

    public static List<WindowInfo> GetTargetableWindows(
        int screenPixelWidth,
        int screenPixelHeight,
        bool allowMaximizedApp = false)
    {
        var results = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (IsWindowSafeToInteract(hWnd, screenPixelWidth, screenPixelHeight, allowMaximizedApp, out WindowInfo? info) &&
                info is not null)
            {
                results.Add(info);
            }
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static bool SafeSetWindowPosition(IntPtr hWnd, int newLeft, int newTop, int screenPixelWidth, int screenPixelHeight)
    {
        if (!IsWindowSafeToInteract(hWnd, screenPixelWidth, screenPixelHeight, allowMaximizedApp: false, out _))
        {
            return false;
        }

        int clampedLeft = Math.Clamp(newLeft, 0, Math.Max(0, screenPixelWidth - 220));
        int clampedTop = Math.Clamp(newTop, 0, Math.Max(0, screenPixelHeight - 140));
        return SetWindowPos(hWnd, IntPtr.Zero, clampedLeft, clampedTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static bool SafeResizeWindow(
        IntPtr hWnd,
        int left,
        int top,
        int width,
        int height,
        int screenPixelWidth,
        int screenPixelHeight)
    {
        if (!IsWindowSafeToInteract(hWnd, screenPixelWidth, screenPixelHeight, allowMaximizedApp: false, out _))
        {
            return false;
        }

        int safeW = Math.Clamp(width, 280, Math.Max(320, screenPixelWidth - 40));
        int safeH = Math.Clamp(height, 200, Math.Max(240, screenPixelHeight - 60));
        int safeL = Math.Clamp(left, 0, Math.Max(0, screenPixelWidth - safeW));
        int safeT = Math.Clamp(top, 0, Math.Max(0, screenPixelHeight - safeH));
        return MoveWindow(hWnd, safeL, safeT, safeW, safeH, true);
    }

    public static bool SafeMinimizeWindow(IntPtr hWnd, int screenPixelWidth, int screenPixelHeight)
    {
        if (!IsWindowSafeToInteract(hWnd, screenPixelWidth, screenPixelHeight, allowMaximizedApp: true, out _))
        {
            return false;
        }

        return ShowWindow(hWnd, SW_MINIMIZE);
    }

    public static bool SafeRestoreWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || IsOwnProcessWindow(hWnd))
        {
            return false;
        }

        return ShowWindow(hWnd, SW_RESTORE);
    }
}
