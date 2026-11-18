using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LangSwitch.Windows;

internal sealed class DesktopInput : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int LowLevelMouseHook = 14;
    private const int MouseMove = 0x0200;

    [Flags]
    private enum ChordKeys
    {
        None = 0,
        LeftAlt = 1,
        LeftShift = 2,
        RightShift = 4,
        Shift = LeftShift | RightShift
    }

    private readonly Action<DesktopTarget, long> switched;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private readonly nint keyboardHook, mouseHook;
    private ChordKeys held;
    private bool overlapped, paused, disposed;
    private long generation;
    private DesktopTarget? origin;

    public DesktopInput(Action<DesktopTarget, long> switched)
    {
        this.switched = switched;
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        nint module = Native.GetModuleHandle(null);
        keyboardHook = Native.SetWindowsHookEx(LowLevelKeyboardHook, keyboardCallback, module, 0);
        if (keyboardHook == 0)
            throw new Win32Exception();

        mouseHook = Native.SetWindowsHookEx(LowLevelMouseHook, mouseCallback, module, 0);
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
        var chordKey = key switch
        {
            Native.LeftAlt => ChordKeys.LeftAlt,
            Native.LeftShift => ChordKeys.LeftShift,
            Native.RightShift => ChordKeys.RightShift,
            _ => ChordKeys.None
        };
        if (chordKey == ChordKeys.None)
        {
            origin = null;
            return;
        }

        if (down)
        {
            if (held == ChordKeys.None && !paused && !OtherModifiersDown())
            {
                origin = DesktopTarget.Capture();
                overlapped = false;
            }
            held |= chordKey;
            if ((held & ChordKeys.LeftAlt) != 0 && (held & ChordKeys.Shift) != 0)
                overlapped = true;
            return;
        }

        held &= ~chordKey;
        if (held != ChordKeys.None)
            return;

        if (overlapped && origin is { } captured)
            switched(captured, generation);
        origin = null;
        overlapped = false;
    }

    private static bool OtherModifiersDown() => Native.IsDown(Native.LeftControl) || Native.IsDown(Native.RightControl) ||
        Native.IsDown(Native.RightAlt) || Native.IsDown(Native.LeftWin) || Native.IsDown(Native.RightWin) ||
        Native.IsDown(Native.LeftButton) || Native.IsDown(Native.RightButton) || Native.IsDown(Native.MiddleButton) ||
        Native.IsDown(Native.XButton1) || Native.IsDown(Native.XButton2);

    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0 && (int)message != MouseMove) // Pointer motion alone does not invalidate the selection.
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
