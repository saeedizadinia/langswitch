using System.Runtime.InteropServices;

namespace LangSwitch.Windows;

internal static partial class Native
{
    internal const uint ClipboardUnicodeText = 13;

    [DllImport("user32")]
    public static extern bool OpenClipboard(nint owner);
    [DllImport("user32")]
    public static extern bool CloseClipboard();
    [DllImport("user32")]
    public static extern bool EmptyClipboard();
    [DllImport("user32")]
    public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")]
    public static extern nint GetClipboardOwner();
    [DllImport("user32", SetLastError = true)]
    public static extern uint EnumClipboardFormats(uint format);
    [DllImport("kernel32")]
    public static extern void SetLastError(uint error);
    [DllImport("user32")]
    public static extern nint GetClipboardData(uint format);
    [DllImport("user32")]
    public static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32")]
    public static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32")]
    public static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32")]
    public static extern nint GlobalLock(nint handle);
    [DllImport("kernel32")]
    public static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32")]
    public static extern nint GlobalFree(nint handle);
}
