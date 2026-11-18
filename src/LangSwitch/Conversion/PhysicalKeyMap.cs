using LangSwitch.Windows;
using Microsoft.Win32;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace LangSwitch.Conversion;

internal sealed class PhysicalKeyMap
{
    // Set-1 scan codes for the QWERTY typing area, including the extra ISO and JIS keys.
    private static readonly int[] ScanCodes =
    [
        0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
        0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x39,
        0x56, 0x73, 0x7D
    ];

    private readonly Dictionary<Rune, string?> replacements = new();

    public static bool IsPhysicalLayout(nint layout)
    {
        // ImmIsIME reports some physical layouts as IME-backed on Windows 11.
        // Check the registered IME file and distinguish extended layout IDs from IME handles.
        uint device = ((uint)layout.ToInt64()) >> 16;
        if ((device & 0xF000) == 0xE000 || Native.ImmGetIMEFileName(layout, 0, 0) != 0)
            return false;
        if ((device & 0xF000) != 0xF000)
            return device != 0;

        using var layouts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
        if (layouts is null)
            return false;
        string id = (device & 0x0FFF).ToString("x4", CultureInfo.InvariantCulture);
        foreach (string name in layouts.GetSubKeyNames())
        {
            using var registration = layouts.OpenSubKey(name);
            if (registration?.GetValue("Layout Id") is string registeredId && id.Equals(registeredId, StringComparison.OrdinalIgnoreCase) &&
                registration.GetValue("Layout File") is string && registration.GetValue("IME File") is null)
                return true;
        }
        return false;
    }

    public PhysicalKeyMap(nint source, nint destination)
    {
        var state = new byte[256];
        var buffer = new char[16];
        for (int modifiers = 0; modifiers < 4; modifiers++)
        {
            bool shift = (modifiers & 1) != 0;
            bool altGr = (modifiers & 2) != 0;
            state[0x10] = state[Native.LeftShift] = shift ? (byte)0x80 : (byte)0; // VK_SHIFT
            state[0x11] = state[Native.LeftControl] = altGr ? (byte)0x80 : (byte)0; // VK_CONTROL
            state[0x12] = state[Native.RightAlt] = altGr ? (byte)0x80 : (byte)0; // VK_MENU

            var layer = new Dictionary<Rune, string?>();
            foreach (int scanCode in ScanCodes)
            {
                string? before = Translate(source, scanCode, state, buffer);
                if (before is null || Rune.DecodeFromUtf16(before, out Rune rune, out int units) != OperationStatus.Done ||
                    units != before.Length)
                    continue;

                string? after = Translate(destination, scanCode, state, buffer);
                if (!layer.TryAdd(rune, after) && layer[rune] != after)
                    layer[rune] = null;
            }

            foreach (var entry in layer)
                replacements.TryAdd(entry.Key, entry.Value);
        }
    }

    private static string? Translate(nint layout, int scan, byte[] state, char[] buffer)
    {
        const uint scanToVirtualKey = 3; // MAPVK_VSC_TO_VK_EX
        const uint noKeyboardStateChange = 4;
        uint key = Native.MapVirtualKeyEx((uint)scan, scanToVirtualKey, layout);
        if (key == 0)
            return null;

        int length = Native.ToUnicodeEx(key, (uint)scan, state, buffer, buffer.Length, noKeyboardStateChange, layout);
        if (length <= 0 || length > buffer.Length || buffer.AsSpan(0, length).ContainsAnyInRange('\0', '\u001f'))
            return null;

        return new string(buffer, 0, length);
    }

    public string Convert(string text)
    {
        var result = new StringBuilder(text.Length);
        ReadOnlySpan<char> remaining = text;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out Rune rune, out int units);
            units = Math.Max(1, units);
            if (status == OperationStatus.Done &&
                replacements.TryGetValue(rune, out string? replacement) && replacement is not null)
                result.Append(replacement);
            else
                result.Append(remaining[..units]);

            remaining = remaining[units..];
        }
        return result.ToString();
    }
}
