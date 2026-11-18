using LangSwitch.Windows;
using System.Runtime.InteropServices;
using System.Windows.Automation.Peers;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace LangSwitch.Checks;

internal sealed class TestEditors : Form
{
    private readonly Panel nativeHost = new() { Left = 24, Top = 46, Width = 390, Height = 84 };

    public TestEditors()
    {
        Text = "LangSwitch test editors";
        ClientSize = new System.Drawing.Size(440, 355);
        Controls.Add(new Label { Text = "Native multiline Edit", Left = 24, Top = 22, AutoSize = true });
        Controls.Add(nativeHost);
        Controls.Add(new Label { Text = "Copy fallback (ValuePattern only)", Left = 24, Top = 144, AutoSize = true });
        Controls.Add(new ElementHost { Left = 24, Top = 169, Width = 390, Height = 32, Child = new CopyOnlyBox { Text = "sghl" } });
        Controls.Add(new Label { Text = "Read-only field", Left = 24, Top = 217, AutoSize = true });
        Controls.Add(new TextBox { Left = 24, Top = 241, Width = 390, Text = "sghl", ReadOnly = true });
        Controls.Add(new Label { Text = "Password field", Left = 24, Top = 285, AutoSize = true });
        Controls.Add(new TextBox { Left = 24, Top = 308, Width = 390, Text = "sghl", UseSystemPasswordChar = true });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        const uint visibleChildMultiline = 0x50001004;
        CreateWindowEx(0x200, "Edit", "sghl\r\nsghl", visibleChildMultiline, 0, 0, 390, 84,
            nativeHost.Handle, 0, Native.GetModuleHandle(null), 0);
    }

    private sealed class CopyOnlyBox : System.Windows.Controls.TextBox
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ValueOnlyPeer(this);

        private sealed class ValueOnlyPeer(CopyOnlyBox owner) : TextBoxAutomationPeer(owner)
        {
            public override object? GetPattern(PatternInterface pattern) =>
                pattern == PatternInterface.Text ? null : base.GetPattern(pattern);
        }
    }

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string text, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint param);
}
