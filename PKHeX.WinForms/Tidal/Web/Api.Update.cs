using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.WinForms.Tidal.Web;

// update.*: TidalHeX updates from its GitHub releases (TidalUpdater).
internal sealed partial class WebApi
{
    private ReleaseInfo? LatestRelease;
    private bool Installing;

    private void RegisterUpdate()
    {
        Bridge.RegisterAsync("update.check", async _ => await CheckForUpdate(manual: true).ConfigureAwait(true));
        Bridge.RegisterAsync("update.install", async _ => await InstallUpdate().ConfigureAwait(true));
        Bridge.Register("update.skip", _ => SkipUpdate());
        Bridge.Register("update.openPage", _ => OpenReleasePage());
#if DEBUG
        // Development only: pretend to be an older version, to test the update flow.
        Bridge.Register("dev.fakeVersion", c => FakeVersion = c.Get<NameArgs>().Name);
#endif
    }

#if DEBUG
    private static string? FakeVersion;
    private static string CurrentVersion => FakeVersion ?? TidalVersion.Current;
#else
    private static string CurrentVersion => TidalVersion.Current;
#endif

    /// <summary>
    /// Startup: clean up after a previous update, then (if enabled) look for a new release in the background and tell the
    /// page about it, unless it's the version the user chose to skip.
    /// </summary>
    public async Task CheckForUpdateAtStartup()
    {
        TidalUpdater.CleanUp();
        if (!Settings.Startup.TidalCheckForUpdates)
            return;
        await Task.Delay(2500).ConfigureAwait(true); // let the page settle first
        var state = await CheckForUpdate(manual: false).ConfigureAwait(true);
        if (state is { Available: true } && state.Latest is { } latest && latest.Version != Settings.Startup.TidalSkippedUpdate)
            Bridge.Emit("updateAvailable", state);
    }

    private async Task<UpdateState> CheckForUpdate(bool manual)
    {
        var state = new UpdateState { Current = CurrentVersion, CurrentDisplay = TidalVersion.Display(CurrentVersion) };
        try
        {
            LatestRelease = await TidalUpdater.GetLatestAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal update check failed: {ex.Message}");
            return state with { Error = manual ? "Couldn't reach GitHub. Check your internet connection and try again." : "unreachable" };
        }

        if (LatestRelease is not { } latest)
            return state with { Checked = true };
        return state with
        {
            Checked = true,
            Available = TidalVersion.Compare(latest.Version, CurrentVersion) > 0,
            Latest = new UpdateRelease
            {
                Version = latest.Version,
                Display = TidalVersion.Display(latest.Version),
                Name = latest.Name,
                Notes = latest.Notes,
                Size = latest.Size,
                Published = latest.Published,
            },
        };
    }

    /// <summary>
    /// Downloads and installs the release found by the last check, then restarts. Asks first if there are unsaved changes
    /// (the restart would lose them). Progress is reported with "updateProgress" events.
    /// </summary>
    private async Task<object?> InstallUpdate()
    {
        if (Installing || LatestRelease is not { } release)
            return false;
        if (!Host.ConfirmDiscardChanges())
            return false;

        Installing = true;
        try
        {
            var progress = new Progress<(long Received, long Total)>(p => Bridge.Emit("updateProgress", new { received = p.Received, total = p.Total }));
            await TidalUpdater.InstallAsync(release, progress, CancellationToken.None).ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            Installing = false;
            WinFormsUtil.Error("TidalHeX can't replace its own file in this folder (it needs permission to write there). " +
                               "Download the new version from the release page instead.", ex.Message);
            OpenReleasePage();
            return false;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            Installing = false;
            WinFormsUtil.Error("The update couldn't be downloaded. Nothing was changed.", ex.Message);
            return false;
        }

        Bridge.Emit("updateProgress", new { received = release.Size, total = release.Size, done = true });
        await Task.Delay(600).ConfigureAwait(true); // let "Restarting…" show
        Host.RestartAfterUpdate();
        return true;
    }

    private bool SkipUpdate()
    {
        if (LatestRelease is not { } latest)
            return false;
        Settings.Startup.TidalSkippedUpdate = latest.Version;
        return true;
    }

    private bool OpenReleasePage()
    {
        var url = LatestRelease?.PageUrl ?? $"https://github.com/{TidalUpdater.Repository}/releases";
        if (!url.StartsWith($"https://github.com/{TidalUpdater.Repository}/", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
