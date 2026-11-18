using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LangSwitch;

internal enum SelectionKind { Unavailable, Rejected, Text }

internal sealed record EditorSelection(int[]? RuntimeId, SelectionKind Kind, string? Text, TextPatternRange? Range = null)
{
    public bool Matches(EditorSelection? other) => other is not null && Kind == other.Kind && Text == other.Text &&
        (RuntimeId is null ? other.RuntimeId is null : other.RuntimeId is not null && RuntimeId.AsSpan().SequenceEqual(other.RuntimeId)) &&
        (Range is null || (other.Range is not null && Range.Compare(other.Range)));
}

internal sealed class EditorProbe
{
    public const int TextLimit = 8192;
    private readonly SemaphoreSlim worker = new(1, 1);

    public async Task<EditorSelection?> ReadAsync(DesktopTarget target, NativeEditor? nativeEditor, EditorSelection? expected = null)
    {
        if (!worker.Wait(0))
            return null;

        var pending = Task.Run(() =>
        {
            try
            {
                var selection = Read(target, nativeEditor?.Writable == true);
                return expected is null || expected.Matches(selection) ? selection : null;
            }
            catch (Exception error) when (IsProviderError(error))
            {
                return null;
            }
            finally { worker.Release(); }
        });
        return await pending.WaitAsync(TimeSpan.FromMilliseconds(450));
    }

    private static EditorSelection? Read(DesktopTarget target, bool nativeWritable)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || (element.Current.ProcessId != target.Process && element.Current.ProcessId != target.FocusProcess))
                return nativeWritable ? new EditorSelection(null, SelectionKind.Unavailable, null) : null;

            if (element.Current.IsPassword || !element.Current.IsEnabled || !element.Current.HasKeyboardFocus)
                return null;

            var text = GetPattern<TextPattern>(element, TextPattern.Pattern);
            var value = GetPattern<ValuePattern>(element, ValuePattern.Pattern);
            var ranges = ReadRanges(text);
            var type = element.Current.ControlType;
            if (!nativeWritable && (!IsEditorType(type, ranges is { Length: 1 }) || !IsWritable(text, value, ranges)))
                return null;

            int[] id = element.GetRuntimeId();
            if (ranges is null)
                return new EditorSelection(id, SelectionKind.Unavailable, null);
            if (ranges.Length != 1 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start, ranges[0], TextPatternRangeEndpoint.End) == 0)
                return new EditorSelection(id, SelectionKind.Rejected, null);

            string selected = ranges[0].GetText(TextLimit + 1);
            return selected.Length is > 0 and <= TextLimit
                ? new EditorSelection(id, SelectionKind.Text, selected, ranges[0])
                : new EditorSelection(id, SelectionKind.Rejected, null);
        }
        catch (Exception error) when (IsProviderError(error))
        {
            return null;
        }
    }

    // Firefox exposes its address bar as an editable combo box. Ordinary dropdowns
    // can have a Value pattern too, so require a text range before accepting a combo box.
    internal static bool IsEditorType(ControlType type, bool hasSingleTextRange) =>
        type == ControlType.Edit || type == ControlType.Document || type == ControlType.Custom ||
        (type == ControlType.ComboBox && hasSingleTextRange);

    private static T? GetPattern<T>(AutomationElement element, AutomationPattern pattern) where T : class =>
        element.TryGetCurrentPattern(pattern, out object result) ? result as T : null;

    private static TextPatternRange[]? ReadRanges(TextPattern? text)
    {
        try
        {
            return text is not null && text.SupportedTextSelection != SupportedTextSelection.None ? text.GetSelection() : null;
        }
        catch (Exception error) when (IsProviderError(error))
        {
            return null;
        }
    }

    private static bool IsWritable(TextPattern? text, ValuePattern? value, TextPatternRange[]? ranges)
    {
        if (value is not null)
            return !value.Current.IsReadOnly;

        var range = ranges is { Length: 1 } ? ranges[0] : text?.DocumentRange;
        return range?.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
    }

    private static bool IsProviderError(Exception error) =>
        error is ElementNotAvailableException or ElementNotEnabledException or COMException or InvalidOperationException or NotSupportedException;
}

internal readonly record struct NativeEditor(nint Window, bool Writable, uint? Selection)
{
    private const uint GetSelection = 0x00B0, ReplaceSelection = 0x00C2, GetPasswordChar = 0x00D2;
    private const uint AbortIfHung = 2;
    private const uint MessageTimeout = 100;

    public static NativeEditor? Inspect(nint window)
    {
        var name = new StringBuilder(128);
        Native.GetClassName(window, name, name.Capacity);
        if (!IsSupportedClass(name.ToString()))
            return null;

        const int editPassword = 0x20, editReadOnly = 0x800;
        bool writable = Native.IsWindowUnicode(window) && Native.IsWindowEnabled(window) &&
            (Native.GetWindowLong(window, -16) & (editPassword | editReadOnly)) == 0 &&
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
