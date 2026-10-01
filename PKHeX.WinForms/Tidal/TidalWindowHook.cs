using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Thread-local CBT hook that themes every WinForms <see cref="Form"/> the moment it is activated,
/// so the ~130 sub-editors in PKHeX pick up the theme without editing each one.
/// </summary>
internal static class TidalWindowHook
{
    private const int WH_CBT = 5;
    private const int HCBT_ACTIVATE = 5;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Keep the delegate rooted for the lifetime of the hook.
    private static HookProc? _proc;
    private static nint _hook;

    public static void Install()
    {
        if (_hook != 0)
            return;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_CBT, _proc, 0, GetCurrentThreadId());
    }

    private static nint Callback(int code, nint wParam, nint lParam)
    {
        if (code == HCBT_ACTIVATE)
        {
            try
            {
                if (Control.FromHandle(wParam) is Form form)
                    TidalTheme.Apply(form);
            }
            catch
            {
                // Never let theming break window activation.
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
