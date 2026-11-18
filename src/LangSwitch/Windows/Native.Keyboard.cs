using System.Runtime.InteropServices;

namespace LangSwitch.Windows;

internal static partial class Native
{
    public const int KeyDown = 0x0100, SysKeyDown = 0x0104;
    public const int LeftButton = 0x01, RightButton = 0x02, MiddleButton = 0x04, XButton1 = 0x05, XButton2 = 0x06;
    public const int LeftShift = 0xA0, RightShift = 0xA1, LeftControl = 0xA2, RightControl = 0xA3;
    public const int LeftAlt = 0xA4, RightAlt = 0xA5, LeftWin = 0x5B, RightWin = 0x5C;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002, UnicodeInput = 0x0004;

    private static readonly nuint InputTag = (nuint)0x4C537743;

    public static bool IsOwnInput(nuint tag) => tag == InputTag;

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public static bool ModifierDown() => IsDown(LeftShift) || IsDown(RightShift) || IsDown(LeftAlt) ||
        IsDown(RightAlt) || IsDown(LeftControl) || IsDown(RightControl) || IsDown(LeftWin) || IsDown(RightWin);

    public static bool SendCopy()
    {
        Input[] keys = [Key(LeftControl, 0, 0), Key('C', 0, 0), Key('C', 0, KeyUp), Key(LeftControl, 0, KeyUp)];
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
            keys[index * 2] = Key(0, text[index], UnicodeInput);
            keys[index * 2 + 1] = Key(0, text[index], UnicodeInput | KeyUp);
        }
        // A partial send cannot be retried without risking duplicate text.
        SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
    }

    private static Input Key(ushort key, ushort scan, uint flags) => new()
    {
        Type = KeyboardInputType,
        Data = new InputData { Keyboard = new KeyboardInput { Key = key, Scan = scan, Flags = flags, Tag = InputTag } }
    };

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

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SetWindowsHookEx(int type, Hook callback, nint module, uint threadId);
    [DllImport("user32")]
    public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")]
    public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")]
    public static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] buffer, int capacity, uint flags, nint layout);
    [DllImport("imm32", CharSet = CharSet.Unicode)]
    public static extern uint ImmGetIMEFileName(nint layout, nint fileName, uint length);
    [DllImport("user32", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int elementSize);
}
