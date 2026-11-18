using LangSwitch.Windows;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LangSwitch.Editing;

internal sealed class EditorProbe
{
    public const int TextLimit = 8192;
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMilliseconds(450);
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
            finally
            {
                worker.Release();
            }
        });
        using var timeout = new CancellationTokenSource();
        if (await Task.WhenAny(pending, Task.Delay(ProviderTimeout, timeout.Token)) != pending)
            throw new TimeoutException();

        timeout.Cancel();
        return await pending;
    }

    private static EditorSelection? Read(DesktopTarget target, bool nativeWritable)
    {
        var element = AutomationElement.FocusedElement;
        if (element is null || !target.OwnsProcess((uint)element.Current.ProcessId))
            return nativeWritable ? new EditorSelection(null, SelectionKind.Unavailable, null) : null;

        var properties = element.Current;
        if (properties.IsPassword || !properties.IsEnabled || !properties.HasKeyboardFocus)
            return null;

        var text = GetPattern<TextPattern>(element, TextPattern.Pattern);
        var value = GetPattern<ValuePattern>(element, ValuePattern.Pattern);
        var ranges = ReadRanges(text);
        if (!nativeWritable && (!IsEditorType(properties.ControlType, ranges is { Length: 1 }) || !IsWritable(text, value, ranges)))
            return null;

        int[] id = element.GetRuntimeId();
        if (ranges is null)
            return new EditorSelection(id, SelectionKind.Unavailable, null);
        if (ranges.Length != 1)
            return new EditorSelection(id, SelectionKind.Rejected, null);

        var range = ranges[0];
        if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
            return new EditorSelection(id, SelectionKind.Rejected, null);

        string selected = range.GetText(TextLimit + 1);
        return selected.Length is > 0 and <= TextLimit
            ? new EditorSelection(id, SelectionKind.Text, selected, range)
            : new EditorSelection(id, SelectionKind.Rejected, null);
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
