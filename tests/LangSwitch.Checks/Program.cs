using LangSwitch.Conversion;
using LangSwitch.Editing;
using LangSwitch.Windows;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace LangSwitch.Checks;

internal static class Checks
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--editors"))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TestEditors());
            return;
        }

        var layouts = new nint[GetKeyboardLayoutList(0, null)];
        GetKeyboardLayoutList(layouts.Length, layouts);
        nint english = Find(layouts, 0x0409);
        nint persian = Find(layouts, 0x0429);
        if (english == 0 || persian == 0)
            throw new InvalidOperationException("Enable US and Persian (Standard) in Windows before running the checks.");
        if (!PhysicalKeyMap.IsPhysicalLayout(english) || !PhysicalKeyMap.IsPhysicalLayout(persian) ||
            PhysicalKeyMap.IsPhysicalLayout((nint)unchecked((int)0xE0010411)))
            throw new InvalidOperationException("Windows keyboard layout and IME classification failed.");

        var toPersian = new PhysicalKeyMap(english, persian);
        var toEnglish = new PhysicalKeyMap(persian, english);
        Equal("سلام", toPersian.Convert("sghl"));
        Equal("sghl", toEnglish.Convert("سلام"));
        Equal("ضض؟۱۲۳", toPersian.Convert("qq?123"));
        Equal("qq?123", toEnglish.Convert("ضض؟۱۲۳"));
        Equal("سلام\t😀\r\nسلام", toPersian.Convert("sghl\t😀\r\nsghl"));
        Equal("\u200C", toPersian.Convert("\u200C"));
        Equal("\ud800", toPersian.Convert("\ud800"));
        CheckRussian(english, layouts);
        Console.WriteLine("Physical key mapping checks passed.");
        CheckHookLifecycle();
        Console.WriteLine("Keyboard and mouse hook lifecycle checks passed.");
        CheckEditorTypes();
        Console.WriteLine("Editable combo box checks passed.");
    }

    private static void CheckEditorTypes()
    {
        if (!EditorProbe.IsEditorType(ControlType.ComboBox, hasSingleTextRange: true))
            throw new InvalidOperationException("An editable combo box can expose a text selection, like the Firefox address bar.");
        if (EditorProbe.IsEditorType(ControlType.ComboBox, hasSingleTextRange: false) ||
            EditorProbe.IsEditorType(ControlType.ListItem, hasSingleTextRange: true))
            throw new InvalidOperationException("Dropdown-only controls and suggestions must be skipped.");
        if (!EditorProbe.IsEditorType(ControlType.Edit, hasSingleTextRange: false))
            throw new InvalidOperationException("A standard editor can use the existing clipboard fallback.");
    }

    private static void CheckHookLifecycle()
    {
        using var input = new DesktopInput((_, _) => { });
        if (!input.IsCurrent(0))
            throw new InvalidOperationException("New hooks must be active.");

        input.Paused = true;
        if (input.IsCurrent(0) || input.IsCurrent(1))
            throw new InvalidOperationException("Pause must invalidate all conversions.");

        input.Paused = false;
        if (input.IsCurrent(0) || !input.IsCurrent(2))
            throw new InvalidOperationException("Resuming must not revive a canceled conversion.");
    }

    private static nint Find(nint[] layouts, int locale) => Array.Find(layouts, layout => ((long)layout & 0xFFFF) == locale);

    private static void CheckRussian(nint english, nint[] layouts)
    {
        nint loaded = Find(layouts, 0x0419);
        nint russian = loaded != 0 ? loaded : LoadKeyboardLayout("00000419", 0);
        if (russian == 0)
        {
            Console.WriteLine("Russian layout unavailable. Enable the standard Russian layout and rerun to check a third layout.");
            return;
        }

        try
        {
            Equal("Привет, мир!", new PhysicalKeyMap(english, russian).Convert("Ghbdtn? vbh!"));
            Equal("Ghbdtn? vbh!", new PhysicalKeyMap(russian, english).Convert("Привет, мир!"));
        }
        finally
        {
            if (loaded == 0)
                UnloadKeyboardLayout(russian);
        }
    }

    private static void Equal(string expected, string result)
    {
        if (!string.Equals(expected, result, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected [{expected}], got [{result}].");
    }

    [DllImport("user32")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint LoadKeyboardLayout(string name, uint flags);
    [DllImport("user32")] private static extern bool UnloadKeyboardLayout(nint layout);
}
