using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BitwardenExtender;

sealed class WindowsEventHook : IDisposable
{
    delegate void WinEventProc(nint hWinEventHook, Events @event, WindowHandle hwnd, int idObject, int idChild, int idEventThread, uint dwmsEventTime);
    public record EventArgs(Events Event, WindowHandle Window, int ObjectId, int ChildId, int EventThreadId, TimeSpan EventTime);
    public delegate void EventHandler(WindowsEventHook sender, EventArgs args);
    public event EventHandler? Event;
    readonly GCHandle _callback;
    readonly nint _eventHook;

    public WindowsEventHook(Events minEvent, Events maxEvent, Flags flags)
    {
        [DllImport("User32.dll", CallingConvention = CallingConvention.Winapi)]
        static extern nint SetWinEventHook(Events eventMin, Events eventMax, nint hmodWinEventProc, nint pfnWinEventProc, uint idProcess, uint idThread, Flags dwFlags);

        WinEventProc callback = RaiseEvent;
        _callback = GCHandle.Alloc(callback);
        _eventHook = SetWinEventHook(minEvent, maxEvent, nint.Zero, Marshal.GetFunctionPointerForDelegate(callback), 0, 0, flags | Flags.OutOfContext);
    }

    public WindowsEventHook(Events @event, Flags flags) : this(@event, @event, flags) { }

    void RaiseEvent(nint hWinEventHook, Events @event, WindowHandle hwnd, int idObject, int idChild, int idEventThread, uint dwmsEventTime)
        => Event?.Invoke(this, new(@event, hwnd, idObject, idChild, idEventThread, TimeSpan.FromMilliseconds(dwmsEventTime)));

    public void Dispose()
    {
        if (!_callback.IsAllocated)
            return;

        [DllImport("User32.dll", CallingConvention = CallingConvention.Winapi)]
        static extern bool UnhookWinEvent(nint hWinEventHook);

        UnhookWinEvent(_eventHook);
        _callback.Free();
    }

    public enum Events : uint
    {
        Foreground = 0x0003
    }

    public enum Flags : uint
    {
        OutOfContext = 0,
        SkipOwnProcess = 2
    }
}
