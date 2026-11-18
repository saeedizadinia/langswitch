using System.Runtime.InteropServices;

namespace LangSwitch.Windows;

internal readonly record struct DesktopTarget(nint Window, nint Focus, uint Thread, uint Process, uint FocusProcess, nint Layout)
{
    private const uint MoveOrMenuMode = 0x1E;
    private static readonly uint CurrentProcessId = Native.GetCurrentProcessId();

    public static DesktopTarget? Capture()
    {
        nint window = Native.GetForegroundWindow();
        if (window == 0)
            return null;

        uint windowThread = Native.GetWindowThreadProcessId(window, out uint process);
        if (windowThread == 0 || process == CurrentProcessId)
            return null;

        var info = new Native.GuiThreadInfo { Size = (uint)Marshal.SizeOf<Native.GuiThreadInfo>() };
        if (!Native.GetGUIThreadInfo(windowThread, ref info) || info.Focus == 0 || (info.Flags & MoveOrMenuMode) != 0)
            return null;

        uint thread = Native.GetWindowThreadProcessId(info.Focus, out uint focusProcess);
        nint layout = thread == 0 ? 0 : Native.GetKeyboardLayout(thread);
        return layout == 0 || Native.GetForegroundWindow() != window
            ? null
            : new DesktopTarget(window, info.Focus, thread, process, focusProcess, layout);
    }

    public bool SameEditor(DesktopTarget other) =>
        Window == other.Window && Focus == other.Focus && Thread == other.Thread &&
        Process == other.Process && FocusProcess == other.FocusProcess;

    public bool OwnsProcess(uint pid) => pid != 0 && (pid == Process || pid == FocusProcess);

    public bool OwnsWindow(nint window) => OwnsProcess(Native.WindowProcess(window));

    public bool HasStandardIntegrity() => Native.IsStandardIntegrity(Process) &&
        (FocusProcess == Process || Native.IsStandardIntegrity(FocusProcess));
}
