using LangSwitch.Windows;
using System.Text;

namespace LangSwitch.Editing;

internal readonly record struct NativeEditor(nint Window, bool Writable, uint? Selection)
{
    private const uint GetSelection = 0x00B0, ReplaceSelection = 0x00C2, GetPasswordChar = 0x00D2;
    private const uint AbortIfHung = 2;
    private const uint MessageTimeout = 100;
    private const int StyleIndex = -16;
    private const int PasswordStyle = 0x20, ReadOnlyStyle = 0x800;

    public static NativeEditor? Inspect(nint window)
    {
        var name = new StringBuilder(128);
        Native.GetClassName(window, name, name.Capacity);
        if (!IsSupportedClass(name.ToString()))
            return null;

        bool writable = Native.IsWindowUnicode(window) && Native.IsWindowEnabled(window) &&
            (Native.GetWindowLong(window, StyleIndex) & (PasswordStyle | ReadOnlyStyle)) == 0 &&
            Native.SendMessageTimeout(window, GetPasswordChar, 0, 0, AbortIfHung, MessageTimeout, out nuint password) != 0 && password == 0;
        if (!writable)
            return new NativeEditor(window, false, null);

        uint? endpoints = Native.SendMessageTimeout(window, GetSelection, 0, 0, AbortIfHung, MessageTimeout, out nuint selection) != 0 &&
            (uint)selection != uint.MaxValue ? (uint)selection : null;
        return new NativeEditor(window, true, endpoints);
    }

    internal static bool IsSupportedClass(string name) => name.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) || name.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RichEditD2DPT", StringComparison.OrdinalIgnoreCase) || name.Equals("RichEditD2D", StringComparison.OrdinalIgnoreCase);

    public bool IsCaret => Selection is uint packed && (packed & 0xFFFF) == (packed >> 16);

    public void Replace(string text) =>
        Native.SendMessageTimeout(Window, ReplaceSelection, 1, text, AbortIfHung, MessageTimeout, out _);
}
