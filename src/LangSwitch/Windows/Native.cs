using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace LangSwitch.Windows;

internal static partial class Native
{
    public static bool IsStandardIntegrity(uint pid)
    {
        using var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out var token))
            return false;

        using (token)
        {
            const uint tokenIntegrityLevel = 25;
            GetTokenInformation(token, tokenIntegrityLevel, 0, 0, out uint size);
            if (size == 0)
                return false;

            nint buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!GetTokenInformation(token, tokenIntegrityLevel, buffer, size, out _))
                    return false;

                nint sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count > 0 && Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) < 0x3000;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static uint WindowProcess(nint window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        private int Left, Top, Right, Bottom;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32")]
    public static extern uint GetCurrentProcessId();
    [DllImport("user32")]
    public static extern nint GetForegroundWindow();
    [DllImport("user32")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32")]
    public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32")]
    public static extern bool IsWindowUnicode(nint window);
    [DllImport("user32")]
    public static extern bool IsWindowEnabled(nint window);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(nint window, int index);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
    [DllImport("kernel32", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("advapi32", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, uint infoClass, nint data, uint length, out uint required);
    [DllImport("advapi32")]
    private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}
