using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace LangSwitch;

internal static class Native
{
    public const int KeyDown = 0x0100, KeyUp = 0x0101, SysKeyDown = 0x0104, SysKeyUp = 0x0105;
    public const int LeftShift = 0xA0, RightShift = 0xA1, LeftControl = 0xA2, RightControl = 0xA3;
    public const int LeftAlt = 0xA4, RightAlt = 0xA5, LeftWin = 0x5B, RightWin = 0x5C;
    internal const uint ClipboardUnicodeText = 13;
    private static readonly nuint InputTag = (nuint)0x4C537743;

    public static bool IsOwnInput(nuint tag) => tag == InputTag;

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public static bool ModifierDown() => IsDown(LeftShift) || IsDown(RightShift) || IsDown(LeftAlt) ||
        IsDown(RightAlt) || IsDown(LeftControl) || IsDown(RightControl) || IsDown(LeftWin) || IsDown(RightWin);

    public static bool SendCopy()
    {
        Input[] keys = [Key(LeftControl, 0, 0), Key('C', 0, 0), Key('C', 0, 2), Key(LeftControl, 0, 2)];
        uint sent = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
        if (sent is 1 or 3)
            SendInput(1, [keys[3]], Marshal.SizeOf<Input>());
        if (sent == 2)
            SendInput(2, [keys[2], keys[3]], Marshal.SizeOf<Input>());
        return sent >= 2;
    }

    public static void SendUnicode(string text)
    {
        var keys = new Input[text.Length * 2];
        for (int index = 0; index < text.Length; index++)
        {
            keys[index * 2] = Key(0, text[index], 4);
            keys[index * 2 + 1] = Key(0, text[index], 4 | 2);
        }
        // A partial send cannot be retried without risking duplicate text.
        SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
    }

    private static Input Key(ushort key, ushort scan, uint flags) => new()
    {
        Type = 1,
        Data = new InputData { Keyboard = new KeyboardInput { Key = key, Scan = scan, Flags = flags, Tag = InputTag } }
    };

    public static bool IsStandardIntegrity(uint pid)
    {
        using var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out var token))
            return false;

        using (token)
        {
            const uint tokenIntegrityLevel = 25;
            GetTokenInformation(token, tokenIntegrityLevel, 0, 0, out uint size);
            if (size == 0)
                return false;

            nint buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!GetTokenInformation(token, tokenIntegrityLevel, buffer, size, out _))
                    return false;

                nint sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count > 0 && Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) < 0x3000;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static uint WindowProcess(nint window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        private int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyHookData
    {
        public uint Key, Scan, Flags, Time;
        public nuint Tag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public nuint Tag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint Tag;
    }

    public delegate nint Hook(int code, nint message, nint data);

    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint SetWindowsHookEx(int type, Hook callback, nint module, uint threadId);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32")] public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32")] public static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] buffer, int capacity, uint flags, nint layout);
    [DllImport("imm32", CharSet = CharSet.Unicode)] public static extern uint ImmGetIMEFileName(nint layout, nint fileName, uint length);
    [DllImport("user32", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int elementSize);
    [DllImport("user32")] public static extern bool IsWindowUnicode(nint window);
    [DllImport("user32")] public static extern bool IsWindowEnabled(nint window);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32", EntryPoint = "GetWindowLongW", SetLastError = true)] public static extern int GetWindowLong(nint window, int index);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] public static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
    [DllImport("kernel32", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("advapi32", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, uint infoClass, nint data, uint length, out uint required);
    [DllImport("advapi32")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32")] private static extern nint GetSidSubAuthority(nint sid, uint index);

    [DllImport("user32")] public static extern bool OpenClipboard(nint owner);
    [DllImport("user32")] public static extern bool CloseClipboard();
    [DllImport("user32")] public static extern bool EmptyClipboard();
    [DllImport("user32")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] public static extern nint GetClipboardOwner();
    [DllImport("user32", SetLastError = true)] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32")] public static extern nint GetClipboardData(uint format);
    [DllImport("user32")] public static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32")] public static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32")] public static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32")] public static extern nint GlobalLock(nint handle);
    [DllImport("kernel32")] public static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32")] public static extern nint GlobalFree(nint handle);
}
