using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LangSwitch;

internal readonly record struct DesktopTarget(nint Window, nint Focus, uint Thread, uint Process, uint FocusProcess, nint Layout)
{
    public static DesktopTarget? Capture()
    {
        nint window = Native.GetForegroundWindow();
        if (window == 0)
            return null;

        uint windowThread = Native.GetWindowThreadProcessId(window, out uint process);
        if (windowThread == 0 || process == Environment.ProcessId)
            return null;

        var info = new Native.GuiThreadInfo { Size = (uint)Marshal.SizeOf<Native.GuiThreadInfo>() };
        if (!Native.GetGUIThreadInfo(windowThread, ref info) || info.Focus == 0 || (info.Flags & 0x1E) != 0)
            return null;

        uint thread = Native.GetWindowThreadProcessId(info.Focus, out uint focusProcess);
        nint layout = thread == 0 ? 0 : Native.GetKeyboardLayout(thread);
        return layout == 0 || Native.GetForegroundWindow() != window
            ? null
            : new DesktopTarget(window, info.Focus, thread, process, focusProcess, layout);
    }

    public bool SameEditor(DesktopTarget other) =>
        Window == other.Window && Focus == other.Focus && Thread == other.Thread &&
        Process == other.Process && FocusProcess == other.FocusProcess;

    public bool OwnsWindow(nint window)
    {
        uint pid = Native.WindowProcess(window);
        return pid != 0 && (pid == Process || pid == FocusProcess);
    }
}

internal sealed class DesktopInput : IDisposable
{
    private readonly Action<DesktopTarget, long> switched;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private readonly nint keyboardHook, mouseHook;
    private int held;
    private bool overlapped, paused, disposed;
    private long generation;
    private DesktopTarget? origin;

    public DesktopInput(Action<DesktopTarget, long> switched)
    {
        this.switched = switched;
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        nint module = Native.GetModuleHandle(null);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, module, 0); // WH_KEYBOARD_LL
        if (keyboardHook == 0)
            throw new Win32Exception();

        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0); // WH_MOUSE_LL
        if (mouseHook == 0)
        {
            var error = new Win32Exception();
            Native.UnhookWindowsHookEx(keyboardHook);
            throw error;
        }
    }

    public bool Paused
    {
        set
        {
            paused = value;
            Invalidate();
        }
    }

    public bool IsCurrent(long expected) => !Volatile.Read(ref paused) && !Volatile.Read(ref disposed) &&
        Volatile.Read(ref generation) == expected;

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<Native.KeyHookData>(data);
            if (!Native.IsOwnInput(key.Tag))
            {
                int kind = (int)message;
                ObserveKey((int)key.Key, kind is Native.KeyDown or Native.SysKeyDown);
            }
        }

        return Native.CallNextHookEx(0, code, message, data);
    }

    private void ObserveKey(int key, bool down)
    {
        Interlocked.Increment(ref generation);
        int bit = key switch
        {
            Native.LeftAlt => 1,
            Native.LeftShift => 2,
            Native.RightShift => 4,
            _ => 0
        };
        if (bit == 0)
        {
            origin = null;
            return;
        }

        if (down)
        {
            if (held == 0 && !paused && !OtherModifiersDown())
            {
                origin = DesktopTarget.Capture();
                overlapped = false;
            }
            held |= bit;
            if ((held & 1) != 0 && (held & 6) != 0)
                overlapped = true;
            return;
        }

        held &= ~bit;
        if (held != 0)
            return;

        if (overlapped && origin is { } captured)
            switched(captured, generation);
        origin = null;
        overlapped = false;
    }

    private static bool OtherModifiersDown() => Native.IsDown(Native.LeftControl) || Native.IsDown(Native.RightControl) ||
        Native.IsDown(Native.RightAlt) || Native.IsDown(Native.LeftWin) || Native.IsDown(Native.RightWin) ||
        Native.IsDown(1) || Native.IsDown(2) || Native.IsDown(4) || Native.IsDown(5) || Native.IsDown(6);

    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && (int)message != 0x0200) // Pointer motion alone does not invalidate the selection.
            Invalidate();
        return Native.CallNextHookEx(0, code, message, data);
    }

    private void Invalidate()
    {
        Interlocked.Increment(ref generation);
        origin = null;
    }

    public void Dispose()
    {
        disposed = true;
        Native.UnhookWindowsHookEx(mouseHook);
        Native.UnhookWindowsHookEx(keyboardHook);
    }
}
