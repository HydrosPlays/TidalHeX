using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PKHeX.Core;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// TidalHeX main window: hosts the HTML UI (<c>wwwroot</c>) in WebView2 and serves it from <c>https://tidal.local/</c>.
/// </summary>
/// <remarks>
/// Static files come from embedded resources; sprites and wallpapers are rendered on request (<see cref="WebImages"/>);
/// everything else goes through the RPC bridge (<see cref="WebBridge"/>, <see cref="WebApi"/>). See API.md.
/// </remarks>
public sealed class TidalWebHost : Form
{
    private const string AppHost = "tidal.local";
    private const string AppOrigin = "https://" + AppHost + "/";
    private const string StartPage = AppOrigin + "index.html";

    /// <summary> Page background; also shown before the first paint to avoid a white flash. </summary>
    private static readonly Color Navy = Color.FromArgb(0x06, 0x22, 0x4A);

    private WebView2 View;
    private readonly WebBridge Bridge;
    private readonly WebApi Api;
    private readonly WebImages Images;
    private readonly ProgramInit Init;

    /// <summary> PKHeX's messages and questions, shown inside the page. </summary>
    internal WebDialogs Dialogs { get; }

    private bool PageReady;
    private bool StartupActionsDone;
    private bool CloseConfirmed;
    private bool Recovering;

    /// <summary> Raised once when the page is up (or failed to start), so the splash screen can close. </summary>
    public event EventHandler? Ready;

    private static PKHeXSettings Settings => Program.Settings;

    public TidalWebHost(StartupArguments startup, ProgramInit init)
    {
        Init = init;

        SuspendLayout();
        Text = "TidalHeX";
        Icon = TidalAssets.AppIcon;
        BackColor = Navy;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        SetInitialBounds();
        View = CreateView();
        Controls.Add(View);
        ResumeLayout(false);
        TidalTheme.Apply(this);

        Bridge = new WebBridge(this);
        Dialogs = new WebDialogs(this, Bridge);
        Dialogs.Attach();
        Api = new WebApi(this, Bridge);
        Images = new WebImages(Api);

        // Same engine setup and initial file loading as the classic window (Main ctor + Program.Main).
        Api.InitializeGlobals();
        Api.LoadInitialFiles(startup);
    }

    private static WebView2 CreateView() => new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Navy,
    };

    /// <summary>
    /// ~1360×860 logical pixels, clamped to the working area of the screen under the cursor, and centered.
    /// </summary>
    private void SetInitialBounds()
    {
        var scale = DeviceDpi / 96f;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min((int)(1000 * scale), area.Width), Math.Min((int)(680 * scale), area.Height));
        var width = Math.Min((int)(1360 * scale), area.Width);
        var height = Math.Min((int)(860 * scale), area.Height);
        Bounds = new Rectangle(area.Left + ((area.Width - width) / 2), area.Top + ((area.Height - height) / 2), width, height);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = InitializeWebViewAsync();
    }

    #region WebView2 setup

    private static string UserDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidalHeX", "WebView2");

    private async Task InitializeWebViewAsync()
    {
        CoreWebView2 core;
        try
        {
            Directory.CreateDirectory(UserDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder).ConfigureAwait(true);
            await View.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            core = View.CoreWebView2;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                OnWebViewUnavailable(ex);
            return;
        }

        ConfigureSettings(core.Settings);
        try
        {
            // Image URLs are versioned per session; never let a previous session's cache answer them.
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: could not clear the WebView2 cache: {ex.Message}");
        }

        core.AddWebResourceRequestedFilter(AppOrigin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += (_, e) => Bridge.Receive(e);
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += OnNavigationCompleted;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += OnPermissionRequested;
        core.ProcessFailed += OnProcessFailed;
        Resize += (_, _) => UpdateMemoryTarget();

        Bridge.Core = core;
        core.Navigate(StartPage);
    }

    /// <summary>
    /// While minimized, WebView2 gets a low memory target (it trims caches and may page out); normal once restored.
    /// Rendering itself already stops while minimized.
    /// </summary>
    private void UpdateMemoryTarget()
    {
        if (View.CoreWebView2 is not { } core)
            return;
        var level = WindowState == FormWindowState.Minimized ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
        try
        {
            if (core.MemoryUsageTargetLevel != level)
                core.MemoryUsageTargetLevel = level;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: memory target not applied: {ex.Message}");
        }
    }

    private static void ConfigureSettings(CoreWebView2Settings s)
    {
#if DEBUG
        const bool debug = true;
#else
        const bool debug = false;
#endif
        s.AreDevToolsEnabled = debug; // F12
        s.AreDefaultContextMenusEnabled = debug;
        s.AreBrowserAcceleratorKeysEnabled = debug;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = true;
    }

    private void OnWebViewUnavailable(Exception ex)
    {
        NotifyPageReady(); // close the splash so the message is visible
        var reason = ex is WebView2RuntimeNotFoundException
            ? "The Microsoft Edge WebView2 Runtime is not installed. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 to use the TidalHeX interface."
            : $"The TidalHeX interface could not start.{Environment.NewLine}{ex.Message}";
        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, reason, "Restart in the classic PKHeX interface?");
        if (prompt != DialogResult.Yes)
            return;
        if (!ConfirmDiscardChanges()) // restarting skips the close prompt; ask about unsaved edits first
            return;
        Settings.Startup.TidalUI = false;
        RestartInClassicMode();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Dialogs.SetPageAlive(false); // a dialog shown by the old page can't be answered anymore
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // The page state is rebuilt from the host (app.init), which still holds the session.
                BeginInvoke(() => View.CoreWebView2?.Reload());
                break;
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                BeginInvoke(RecreateView);
                break;
        }
    }

    /// <summary>
    /// The browser process died: replace the WebView. The editing session lives in the host and is unaffected.
    /// </summary>
    private void RecreateView()
    {
        if (Recovering || IsDisposed)
            return;
        Recovering = true;
        try
        {
            Bridge.Core = null;
            var old = View;
            View = CreateView();
            Controls.Add(View);
            Controls.Remove(old);
            old.Dispose();
            _ = InitializeWebViewAsync();
        }
        finally
        {
            Recovering = false;
        }
    }

    #endregion

    #region Resources (https://tidal.local/)

    private static bool IsAppUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(AppHost, StringComparison.OrdinalIgnoreCase);

    private static bool IsImageRoute(string path) => path.StartsWith("sprite/", StringComparison.OrdinalIgnoreCase)
                                                   || path.StartsWith("wallpaper/", StringComparison.OrdinalIgnoreCase);

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || !IsAppUri(uri))
            return;

        var env = View.CoreWebView2.Environment;
        try
        {
            if (e.Request.Method is not ("GET" or "HEAD"))
            {
                e.Response = CreateResponse(env, 405, "Method Not Allowed");
                return;
            }

            var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            if (IsImageRoute(path))
            {
                var query = ParseQuery(uri.Query);
                if (!Images.TryRender(path, query, out var png))
                {
                    e.Response = CreateResponse(env, 404, "Not Found");
                    return;
                }
                // Versioned URLs (?v=) never change content; unversioned ones must always be fresh.
                var cache = query.ContainsKey("v") ? "max-age=31536000, immutable" : "no-store";
                e.Response = CreateResponse(env, png, "image/png", cache);
                return;
            }

            if (!WebContent.TryGet(path, out var data, out var mime))
            {
                e.Response = CreateResponse(env, 404, "Not Found");
                return;
            }
            e.Response = CreateResponse(env, data, mime, "no-cache");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: {uri} failed: {ex}");
            e.Response = CreateResponse(env, 500, "Internal Server Error");
        }
    }

    private static CoreWebView2WebResourceResponse CreateResponse(CoreWebView2Environment env, byte[] data, string mime, string cache)
    {
        var headers = $"Content-Type: {mime}\r\nCache-Control: {cache}\r\nX-Content-Type-Options: nosniff";
        return env.CreateWebResourceResponse(new MemoryStream(data, false), 200, "OK", headers);
    }

    private static CoreWebView2WebResourceResponse CreateResponse(CoreWebView2Environment env, int status, string reason)
        => env.CreateWebResourceResponse(new MemoryStream([], false), status, reason, "Content-Type: text/plain\r\nCache-Control: no-store");

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }
        return result;
    }

    #endregion

    #region Navigation

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            e.Cancel = true;
            return;
        }
        if (IsAppUri(uri))
            return;

        // Never leave tidal.local.
        e.Cancel = true;
        if (uri.IsFile)
        {
            // A file dropped where the page didn't handle the drop: open it rather than navigating to it.
            var path = uri.LocalPath;
            BeginInvoke(() => Bridge.RunExclusive(() => Api.OpenFromPath(path)));
            return;
        }
        OpenExternal(uri);
    }

    private static void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            e.Cancel = true;
            return;
        }
        if (IsAppUri(uri) || uri.Scheme is "about" or "data" or "blob")
            return;
        e.Cancel = true;
        OpenExternal(uri);
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // no popup windows; links open in the default browser
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && !IsAppUri(uri))
            OpenExternal(uri);
    }

    private static void OpenExternal(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: could not open {uri}: {ex.Message}");
        }
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => NotifyPageReady();

    /// <summary>
    /// Once the page has booted (<c>app.init</c> / <c>app.ready</c>), shows the classic startup popups over it.
    /// </summary>
    internal void ScheduleStartupActions()
    {
        if (StartupActionsDone)
            return;
        StartupActionsDone = true;
        BeginInvoke(() => Bridge.RunExclusive(RunStartupActions));
    }

    /// <summary>
    /// Called when the page is up (first navigation or <c>app.init</c>): closes the splash screen.
    /// </summary>
    internal void NotifyPageReady()
    {
        if (PageReady)
            return;
        PageReady = true;
        Ready?.Invoke(this, EventArgs.Empty);
        Activate();
    }

    #endregion

    #region Startup popups (Program.Main → main.Shown)

    private void RunStartupActions()
    {
        if (Init.HaX)
            WarnBehavior();
        else if (Init.ShowChangelog)
            ShowChangelog();
        else if (Init.BackupPrompt)
            PromptBackup(Settings.LocalResources.GetBackupPath());

        _ = CheckForUpdatesAsync();
    }

    private void WarnBehavior()
    {
        var page = new TaskDialogPage
        {
            Caption = MsgProgramIllegalModeActive,
            Text = MsgProgramIllegalModeBehave,
            Icon = TaskDialogIcon.Shield,
            DefaultButton = TaskDialogButton.OK,
            Buttons = [TaskDialogButton.OK],
            AllowCancel = true,
        };
        TaskDialog.ShowDialog(this, page);
    }

    private void ShowChangelog()
    {
        using var form = new About(AboutPage.Changelog);
        form.ShowDialog(this);
    }

    private static void PromptBackup(string folder)
    {
        if (Directory.Exists(folder))
            return;
        if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, string.Format(MsgBackupCreateLocation, folder), MsgBackupCreateQuestion))
            return;

        try
        {
            Directory.CreateDirectory(folder);
            WinFormsUtil.Alert(MsgBackupSuccess, string.Format(MsgBackupDelete, folder));
        }
        catch (Exception ex)
        // Maybe they put their exe in a folder that we can't create files/folders to.
        { WinFormsUtil.Error($"{MsgBackupUnable} @ {folder}", ex); }
    }

    private async Task CheckForUpdatesAsync()
    {
        Version? latest;
        // User might not be connected to the internet or with a flaky connection.
        try { latest = await Task.Run(UpdateUtil.GetLatestPKHeXVersion).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception while checking for latest version: {ex}");
            return;
        }
        if (latest is null || latest <= Program.CurrentVersion || IsDisposed)
            return;

        var date = $"{2000 + latest.Major:00}{latest.Minor:00}{latest.Build:00}";
        Api.Toast("info", $"{MsgProgramUpdateAvailable} {date}");
    }

    #endregion

    #region Closing

    /// <summary>
    /// Main.Main_FormClosing's prompt: asks before discarding unsaved save or editor changes.
    /// </summary>
    internal bool ConfirmDiscardChanges()
    {
        var session = Api.Session;
        if (!session.SAV.State.Edited && !session.EditorDirty)
            return true;
        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgProgramCloseUnsaved, MsgProgramCloseConfirm);
        return prompt == DialogResult.Yes;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel)
            return;
        if (Dialogs.IsOpen) // answer the open question first
        {
            e.Cancel = true;
            return;
        }

        try
        {
            if (!CloseConfirmed && !ConfirmDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            CloseConfirmed = true;
            Dialogs.SetPageAlive(false);
            Dialogs.Detach();
            Api.Shutdown();
            SaveSettings();
        }
        catch
        {
            // Ignore; program is shutting down.
        }
    }

    /// <summary>
    /// Persists the settings file like the classic window does on close.
    /// </summary>
    private static void SaveSettings()
    {
        try
        {
            // Off the UI thread (SaveSettings is async), but finish before the process can exit or restart.
            Task.Run(() => PKHeXSettings.SaveSettings(Program.PathConfig, Settings)).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: saving settings failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves settings and restarts the program (it will start in the classic window if <see cref="StartupSettings.TidalUI"/> is off).
    /// </summary>
    internal void RestartInClassicMode()
    {
        CloseConfirmed = true;
        SaveSettings();
        BeginInvoke(Application.Restart);
    }

    #endregion
}
