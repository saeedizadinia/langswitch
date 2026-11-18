using LangSwitch.Editing;
using LangSwitch.Windows;
using System.Diagnostics;

namespace LangSwitch.Conversion;

internal sealed class ConversionCoordinator(nint clipboardWindow)
{
    private const int LayoutSwitchTimeoutMilliseconds = 500;
    private const int ConversionTimeoutMilliseconds = 1400;
    private const int LayoutPollIntervalMilliseconds = 15;
    private const int LayoutCacheCapacity = 32;

    private readonly EditorProbe editor = new();
    private readonly ClipboardCopy clipboard = new(clipboardWindow);
    private readonly Dictionary<(nint Source, nint Destination), PhysicalKeyMap> maps = new();
    private readonly SemaphoreSlim transaction = new(1, 1);

    public async Task ConvertAfterSwitchAsync(DesktopTarget origin, Func<bool> inputIsCurrent)
    {
        var release = Stopwatch.StartNew();
        await transaction.WaitAsync();
        try
        {
            await ConvertSelectionAsync(origin, inputIsCurrent, release);
        }
        finally
        {
            transaction.Release();
        }
    }

    private async Task ConvertSelectionAsync(DesktopTarget origin, Func<bool> inputIsCurrent, Stopwatch release)
    {
        var destination = await WaitForLayoutAsync(origin, inputIsCurrent, release);
        if (destination is null)
            return;

        bool TargetIsCurrent() => release.ElapsedMilliseconds <= ConversionTimeoutMilliseconds && inputIsCurrent() &&
            DesktopTarget.Capture() == destination;

        if (!origin.HasStandardIntegrity() || !TargetIsCurrent())
            return;

        var map = GetMap(origin.Layout, destination.Value.Layout);
        if (map is null)
            return;

        var native = NativeEditor.Inspect(origin.Focus);
        if (native is { Writable: false } or { IsCaret: true })
            return;

        var selection = await editor.ReadAsync(origin, native);
        if (selection is null || selection.Kind == SelectionKind.Rejected || !TargetIsCurrent())
            return;

        string? text = selection.Kind == SelectionKind.Text
            ? selection.Text
            : await clipboard.ReadAsync(origin, TargetIsCurrent, inputIsCurrent);
        if (text is null || text.IndexOf('\0') >= 0 || !TargetIsCurrent())
            return;

        string replacement = map.Convert(text);
        if (text == replacement || replacement.Length > EditorProbe.TextLimit ||
            (native is null && replacement.Any(char.IsControl)))
            return;

        if (await editor.ReadAsync(origin, native, selection) is null || !TargetIsCurrent() || Native.ModifierDown())
            return;

        if (native is { } edit)
        {
            if (NativeEditor.Inspect(origin.Focus) is { Writable: true, IsCaret: false } latest &&
                edit.Selection == latest.Selection && TargetIsCurrent())
                latest.Replace(replacement);
        }
        else
        {
            Native.SendUnicode(replacement);
        }
    }

    private PhysicalKeyMap? GetMap(nint source, nint destination)
    {
        var pair = (source, destination);
        if (maps.TryGetValue(pair, out var map))
            return map;
        if (!PhysicalKeyMap.IsPhysicalLayout(source) || !PhysicalKeyMap.IsPhysicalLayout(destination))
            return null;

        map = new PhysicalKeyMap(source, destination);
        if (maps.Count >= LayoutCacheCapacity)
            maps.Clear();
        maps[pair] = map;
        return map;
    }

    private static async Task<DesktopTarget?> WaitForLayoutAsync(DesktopTarget origin, Func<bool> current, Stopwatch release)
    {
        while (release.ElapsedMilliseconds <= LayoutSwitchTimeoutMilliseconds && current())
        {
            if (DesktopTarget.Capture() is not { } foreground || !origin.SameEditor(foreground))
                return null;
            if (foreground.Layout != origin.Layout)
                return foreground;
            await Task.Delay(LayoutPollIntervalMilliseconds);
        }
        return null;
    }
}
