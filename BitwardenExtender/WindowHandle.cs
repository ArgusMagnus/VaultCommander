using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BitwardenExtender;

public enum ShowWindowCommands
{
    /// <summary>
    /// Minimizes a window, even if the thread that owns the window is not responding. This flag should only be used when minimizing windows from a different thread.
    /// </summary>
    SW_FORCEMINIMIZE = 11,

    /// <summary>
    /// Hides the window and activates another window.
    /// </summary>
    SW_HIDE = 0,

    /// <summary>
    /// Maximizes the specified window.
    /// </summary>
    SW_MAXIMIZE = 3,

    /// <summary>
    /// Minimizes the specified window and activates the next top-level window in the Z order.
    /// </summary>
    SW_MINIMIZE = 6,

    /// <summary>
    /// Activates and displays the window. If the window is minimized or maximized, the system restores it to its original size and position. An application should specify this flag when restoring a minimized window.
    /// </summary>
    SW_RESTORE = 9,

    /// <summary>
    /// Activates the window and displays it in its current size and position.
    /// </summary>
    SW_SHOW = 5,

    /// <summary>
    /// Sets the show state based on the SW_ value specified in the STARTUPINFO structure passed to the CreateProcess function by the program that started the application.
    /// </summary>
    SW_SHOWDEFAULT = 10,

    /// <summary>
    /// Activates the window and displays it as a maximized window.
    /// </summary>
    SW_SHOWMAXIMIZED = 3,

    /// <summary>
    /// Activates the window and displays it as a minimized window.
    /// </summary>
    SW_SHOWMINIMIZED = 2,

    /// <summary>
    /// Displays the window as a minimized window. This value is similar to SW_SHOWMINIMIZED, except the window is not activated.
    /// </summary>
    SW_SHOWMINNOACTIVE = 7,

    /// <summary>
    /// Displays the window in its current size and position. This value is similar to SW_SHOW, except that the window is not activated.
    /// </summary>
    SW_SHOWNA = 8,

    /// <summary>
    /// Displays a window in its most recent size and position. This value is similar to SW_SHOWNORMAL, except that the window is not activated.
    /// </summary>
    SW_SHOWNOACTIVATE = 4,

    /// <summary>
    /// Activates and displays a window. If the window is minimized or maximized, the system restores it to its original size and position. An application should specify this flag when displaying the window for the first time.
    /// </summary>
    SW_SHOWNORMAL = 1,
}

[DebuggerDisplay("{_handle} {Text ?? string.Empty}")]
public readonly struct WindowHandle : IEquatable<WindowHandle>
{
    public static readonly WindowHandle Null = new WindowHandle(nint.Zero);

    readonly nint _handle;

    public WindowHandle(nint handle) => _handle = handle;

    public unsafe WindowHandle(void* handle)
        : this(new nint(handle)) { }

    public static WindowHandle FromWpfWindow(Window window) => new WindowHandle(new WindowInteropHelper(window).Handle);

    public bool IsEnabled => IsWindowEnabled(this);
    public bool HasMouseCaptured => GetWindowWithMouseCapture() == this;

    public bool TryGetText([NotNullWhen(true)] out string? text)
    {
        text = null;
        if (_handle == nint.Zero)
            return false;

        text = string.Empty;
        var size = GetWindowTextLength(this);
        if (size is 0)
            return Marshal.GetLastWin32Error() is 0;

        text = new string('\0', size);

        unsafe
        {
            fixed (char* ptr = text)
            {
                if (GetWindowText(this, ptr, size + 1) == 0)
                    return false;
            }
        }

        return true;

    }

    public string Text => TryGetText(out var text) ? text : throw new System.ComponentModel.Win32Exception();

    /// <summary>
    /// Enables/disables the window.
    /// </summary>
    /// <returns><c>true</c>, if the window was previously enabled, <c>false</c> otherwise.</returns>
    public bool Enable(bool enable) => !EnableWindow(this, enable);

    public void SetAsOwnerOf(Window window) => new WindowInteropHelper(window).Owner = _handle;

    const int GWLP_HWNDPARENT = -8;
    public void SetOwner(WindowHandle window) => SetWindowLongPtr(this, GWLP_HWNDPARENT, window._handle);
    public void SetAsOwnerOf(WindowHandle window) => window.SetOwner(this);
    public WindowHandle Owner => new WindowHandle(GetWindowLongPtr(this, GWLP_HWNDPARENT));

    public WindowHandle SetParent(WindowHandle newParent) => SetParent(this, newParent);
    public WindowHandle Parent => GetParent(this);

    public string? ClassName
    {
        get
        {
            var sb = new StringBuilder(256);
            if (GetClassName(this, sb, sb.Capacity) == 0)
                return null;
            return sb.ToString();
        }
    }

    public bool IsTopMost => (GetWindowLongPtr(this, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) == WS_EX_TOPMOST;
    public bool SetTopMost(bool topMost) => SetWindowPos(this, new nint(topMost ? HWND_TOPMOST : HWND_NOTOPMOST), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);

    public void Activate()
    {
        if (SetActiveWindow(this) == Null)
            throw new System.ComponentModel.Win32Exception();
    }

    public bool Focus() => SetForegroundWindow(this);

    public bool Show(ShowWindowCommands command) => ShowWindow(this, command);

    public bool Equals(WindowHandle other) => this == other;
    public override bool Equals(object? obj) => obj is WindowHandle wnd ? wnd._handle == _handle : false;
    public override int GetHashCode() => _handle.GetHashCode();

    public static bool operator ==(WindowHandle lhs, WindowHandle rhs) => lhs._handle == rhs._handle;
    public static bool operator !=(WindowHandle lhs, WindowHandle rhs) => lhs._handle != rhs._handle;

    public static implicit operator WindowHandle(Window window) => FromWpfWindow(window);

    const string User32Dll = "User32.dll";
    const string Kernel32Dll = "kernel32.dll";

    [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern WindowHandle FindWindow(string? className, string? windowName);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern WindowHandle GetActiveWindow();

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern WindowHandle SetActiveWindow(WindowHandle newActive);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool SetForegroundWindow(WindowHandle newForeground);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool ShowWindow(WindowHandle window, ShowWindowCommands command);

    [DllImport(Kernel32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern WindowHandle GetConsoleWindow();

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool EnableWindow(WindowHandle handel, bool enable);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool IsWindowEnabled(WindowHandle handel);

    [DllImport(User32Dll, EntryPoint = "GetCapture", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern WindowHandle GetWindowWithMouseCapture();

    [DllImport(User32Dll, EntryPoint = "SetCapture", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern WindowHandle SetMouseCapture(WindowHandle handle);

    [DllImport(User32Dll, EntryPoint = "ReleaseCapture", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern bool ReleaseMouse();

    public MouseCapture CaptureMouse() => new MouseCapture(this);

    public readonly struct MouseCapture : IDisposable
    {
        public MouseCapture(WindowHandle handle) => SetMouseCapture(handle);
        public void Dispose() => ReleaseMouse();
    }

    [DllImport(User32Dll, EntryPoint = "GetWindowTextLengthW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern int GetWindowTextLength(WindowHandle handle);

    [DllImport(User32Dll, EntryPoint = "GetWindowTextW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static unsafe extern int GetWindowText(WindowHandle handle, char* buffer, int maxCount);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern WindowHandle SetParent(WindowHandle child, WindowHandle newParent);

    [DllImport(User32Dll, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern WindowHandle GetParent(WindowHandle child);

    [DllImport(User32Dll, EntryPoint = "SetWindowLongW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern nint SetWindowLong32(WindowHandle handle, int nIndex, nint value);

    [DllImport(User32Dll, EntryPoint = "SetWindowLongPtrW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern nint SetWindowLong64(WindowHandle handle, int nIndex, nint value);

    static nint SetWindowLongPtr(WindowHandle handle, int nIndex, nint value)
    {
        if (Environment.Is64BitProcess)
            return SetWindowLong64(handle, nIndex, value);
        else
            return SetWindowLong32(handle, nIndex, value);
    }

    [DllImport(User32Dll, EntryPoint = "GetWindowLongW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern nint GetWindowLong32(WindowHandle hwnd, int nIndex);

    [DllImport(User32Dll, EntryPoint = "GetWindowLongPtrW", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern nint GetWindowLong64(WindowHandle hwnd, int nIndex);

    static nint GetWindowLongPtr(WindowHandle hwnd, int nIndex)
    {
        if (Environment.Is64BitProcess)
            return GetWindowLong64(hwnd, nIndex);
        else
            return GetWindowLong32(hwnd, nIndex);
    }


    [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern int GetClassName(WindowHandle hwnd, StringBuilder buffer, int maxCount);


    [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool SetWindowPos(WindowHandle hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    delegate bool EnumWindowsProc(WindowHandle hwnd, nint lParam);

    public static bool EnumWindows(Func<WindowHandle, bool> func) => EnumWindows((hwnd, lParam) => func(hwnd), 0);

    public static bool FindWindow(Predicate<WindowHandle> predicate, out WindowHandle hwnd)
    {
        var result = Null;
        EnumWindows(x =>
        {
            if (predicate(x))
            {
                result = x;
                return false;
            }
            return true;
        });
        hwnd = result;
        return hwnd != Null;
    }

    public static WindowHandle FindWindow(Predicate<WindowHandle> predicate) => FindWindow(predicate, out var window) ? window : Null;

    [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    static extern uint GetWindowThreadProcessId(WindowHandle hwnd, out uint processId);

    public (int ThreadId, int ProcessId) GetThreadAndProcessId()
    {
        var threadId = GetWindowThreadProcessId(this, out var processId);
        if (threadId is 0)
            throw new System.ComponentModel.Win32Exception();
        return unchecked(((int)threadId, (int)processId));
    }

    public bool TryGetThreadAndProcessId(out int threadId, out int processId)
    {
        threadId = unchecked((int)GetWindowThreadProcessId(this, out var pid));
        processId = unchecked((int)pid);
        return threadId is not 0;
    }

    public bool TryGetWindow(GW cmd, out WindowHandle hwnd)
    {
        [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        static extern WindowHandle GetWindow(WindowHandle hwnd, GW cmd);

        hwnd = GetWindow(this, cmd);
        return hwnd != Null;
    }

    public WindowHandle GetWindow(GW cmd) => TryGetWindow(cmd, out var hwnd) ? hwnd : throw new System.ComponentModel.Win32Exception();

    public bool IsVisible
    {
        get
        {
            [DllImport(User32Dll, CharSet = CharSet.Auto, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
            static extern bool IsWindowVisible(WindowHandle hwnd);
            return IsWindowVisible(this);
        }
    }

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOPMOST = 0x00000008;
    const long HWND_TOPMOST = -1;
    const long HWND_NOTOPMOST = -2;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint GW_HWNDNEXT = 2;
    const uint GW_HWNDPREV = 3;

    public enum GW : uint
    {
        First,
        Last,
        Next,
        Previous,
        Owner,
        Child,
        EnabledPopup
    }
}
