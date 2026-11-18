using System.Diagnostics;

namespace LangSwitch;

internal sealed class Conversion(nint clipboardWindow)
{
    private readonly EditorProbe editor = new();
    private readonly ClipboardCopy clipboard = new(clipboardWindow);
    private readonly Dictionary<(nint Source, nint Destination), PhysicalKeyMap> maps = new();
    private readonly SemaphoreSlim transaction = new(1, 1);

    public async Task RunAsync(DesktopTarget origin, Func<bool> inputIsCurrent)
    {
        var release = Stopwatch.StartNew();
        await transaction.WaitAsync();
        try
        {
            var destination = await WaitForLayoutAsync(origin, inputIsCurrent, release);
            if (destination is null)
                return;

            bool Current() => release.ElapsedMilliseconds <= 1400 && inputIsCurrent() &&
                DesktopTarget.Capture() == destination;

            if (!Native.IsStandardIntegrity(origin.Process) ||
                (origin.FocusProcess != origin.Process && !Native.IsStandardIntegrity(origin.FocusProcess)) || !Current())
                return;

            var pair = (origin.Layout, destination.Value.Layout);
            if (!maps.TryGetValue(pair, out var map))
            {
                if (!PhysicalKeyMap.IsPhysicalLayout(pair.Item1) || !PhysicalKeyMap.IsPhysicalLayout(pair.Item2))
                    return;
                map = new PhysicalKeyMap(pair.Item1, pair.Item2);
                if (maps.Count >= 32)
                    maps.Clear();
                maps[pair] = map;
            }

            var native = NativeEditor.Inspect(origin.Focus);
            if (native is { Writable: false } or { IsCaret: true })
                return;

            var selection = await editor.ReadAsync(origin, native);
            if (selection is null || selection.Kind == SelectionKind.Rejected || !Current())
                return;

            string? text = selection.Kind == SelectionKind.Text ? selection.Text : await clipboard.ReadAsync(origin, Current, inputIsCurrent);
            if (text is null || text.Contains('\0') || !Current())
                return;

            string replacement = map.Convert(text);
            if (text == replacement || replacement.Length > EditorProbe.TextLimit ||
                (native is null && replacement.Any(char.IsControl)))
                return;

            if (await editor.ReadAsync(origin, native, selection) is null || !Current() || Native.ModifierDown())
                return;

            if (native is { } edit)
            {
                if (NativeEditor.Inspect(origin.Focus) is { Writable: true, IsCaret: false } latest &&
                    edit.Selection == latest.Selection && Current())
                    latest.Replace(replacement);
            }
            else
            {
                Native.SendUnicode(replacement);
            }
        }
        finally
        {
            transaction.Release();
        }
    }

    private static async Task<DesktopTarget?> WaitForLayoutAsync(DesktopTarget origin, Func<bool> current, Stopwatch release)
    {
        while (release.ElapsedMilliseconds <= 500 && current())
        {
            if (DesktopTarget.Capture() is not { } foreground || !origin.SameEditor(foreground))
                return null;
            if (foreground.Layout != origin.Layout)
                return foreground;
            await Task.Delay(15);
        }
        return null;
    }
}
