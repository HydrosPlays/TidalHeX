using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static PKHeX.WinForms.WinFormsUtil;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Shows PKHeX's messages and yes/no questions (<see cref="WinFormsUtil.Alert"/>, <see cref="WinFormsUtil.Prompt"/>,
/// <see cref="WinFormsUtil.Error(ReadOnlySpan{string?})"/>) as dialogs inside the page instead of Windows message boxes.
/// </summary>
/// <remarks>
/// Callers expect an answer synchronously, so this runs a message loop until the page answers. The answer arrives as
/// an immediate bridge notification (<c>dialog.answer</c>), not a queued call, because the queue is blocked by the
/// handler that is waiting. When a classic window is in front, the normal message box is used so it isn't hidden.
/// </remarks>
internal sealed class WebDialogs
{
    private readonly TidalWebHost Host;
    private readonly WebBridge Bridge;
    private readonly Func<MessageKind, MessageBoxButtons, string, string?, DialogResult?> Hook;

    private int NextId;
    private int WaitingId;
    private DialogResult? Answer;
    private MessageBoxButtons WaitingButtons;
    private bool PageAlive;

    public WebDialogs(TidalWebHost host, WebBridge bridge)
    {
        Host = host;
        Bridge = bridge;
        Hook = Show;
        bridge.RegisterImmediate("dialog.answer", c =>
        {
            var a = c.Get<DialogAnswerArgs>();
            if (a.Id == WaitingId)
                Answer = Parse(a.Result);
        });
    }

    /// <summary> True while the page is showing a dialog and PKHeX is waiting for the answer. </summary>
    public bool IsOpen => WaitingId != 0;

    public void Attach() => MessageHook = Hook;

    /// <summary>
    /// The page's app is running and listening for dialogs (set by app.init/app.ready). Cleared when the page
    /// process dies or reloads, which also cancels a waiting dialog with its safe answer.
    /// </summary>
    public void SetPageAlive(bool alive)
    {
        PageAlive = alive;
        if (!alive && WaitingId != 0 && Answer is null)
            Answer = GetFallback(WaitingButtons);
    }

    public void Detach()
    {
        if (MessageHook == Hook)
            MessageHook = null;
    }

    private bool CanShow()
    {
        if (Host.IsDisposed || !Host.IsHandleCreated || !PageAlive || Host.InvokeRequired)
            return false;
        if (!Host.Visible || !Host.Enabled || Host.WindowState == FormWindowState.Minimized)
            return false;
        // A classic window (sub-editor, settings...) is in front: its message box belongs over it.
        return !Application.OpenForms.Cast<Form>().Any(f => !ReferenceEquals(f, Host) && f.Visible && f is not SplashScreen);
    }

    private DialogResult? Show(MessageKind kind, MessageBoxButtons buttons, string text, string? details)
    {
        if (!CanShow())
            return null;

        // Dialogs can nest (e.g. a close request while one is open); restore the outer one afterwards.
        var (outerId, outerAnswer, outerButtons) = (WaitingId, Answer, WaitingButtons);
        var id = WaitingId = ++NextId;
        Answer = null;
        WaitingButtons = buttons;
        try
        {
            Bridge.Emit("dialog", new
            {
                id,
                kind = kind.ToString().ToLowerInvariant(),
                text,
                details,
                buttons = GetButtons(buttons),
            });
            while (Answer is null && !Host.IsDisposed && PageAlive)
            {
                Application.DoEvents();
                if (Answer is null)
                    WaitMessage(); // sleep until the next message; no busy loop
            }
            return Answer ?? GetFallback(buttons);
        }
        finally
        {
            (WaitingId, Answer, WaitingButtons) = (outerId, outerAnswer, outerButtons);
        }
    }

    private static string[] GetButtons(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OKCancel => ["ok", "cancel"],
        MessageBoxButtons.YesNo => ["yes", "no"],
        MessageBoxButtons.YesNoCancel => ["yes", "no", "cancel"],
        MessageBoxButtons.RetryCancel => ["retry", "cancel"],
        MessageBoxButtons.AbortRetryIgnore => ["abort", "retry", "ignore"],
        MessageBoxButtons.CancelTryContinue => ["cancel", "try", "continue"],
        _ => ["ok"],
    };

    /// <summary> The safe answer if the page goes away while a dialog is open. </summary>
    private static DialogResult GetFallback(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.YesNo => DialogResult.No,
        MessageBoxButtons.OK => DialogResult.OK,
        _ => DialogResult.Cancel,
    };

    private static DialogResult Parse(string result) => result switch
    {
        "ok" => DialogResult.OK,
        "yes" => DialogResult.Yes,
        "no" => DialogResult.No,
        "retry" => DialogResult.Retry,
        "abort" => DialogResult.Abort,
        "ignore" => DialogResult.Ignore,
        "try" => DialogResult.TryAgain,
        "continue" => DialogResult.Continue,
        _ => DialogResult.Cancel,
    };

    [DllImport("user32.dll")]
    private static extern bool WaitMessage();
}
