using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Updates TidalHeX from its GitHub releases: finds the newest release that has a <see cref="AssetName"/> attached,
/// downloads it next to the running exe and swaps the two (a running exe can be renamed, not overwritten).
/// </summary>
internal static class TidalUpdater
{
    public const string Repository = "HydrosPlays/TidalHeX";
    public const string AssetName = "TidalHeX.exe";
    private const string ReleasesApi = $"https://api.github.com/repos/{Repository}/releases?per_page=20";
    private const string DownloadSuffix = ".update.tmp";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"TidalHeX/{TidalVersion.Current}"); // GitHub requires a user agent
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// The newest release (pre-releases included: every TidalHeX release so far is one) with the exe attached,
    /// or null if there is none. Throws when GitHub can't be reached.
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var stream = await Http.GetStreamAsync(ReleasesApi, timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);

        ReleaseInfo? best = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!TidalVersion.TryParse(tag, out _, out _))
                continue;
            var asset = FindAsset(release);
            if (asset is null)
                continue;
            if (best is not null && TidalVersion.Compare(tag, best.Version) <= 0)
                continue;

            best = new ReleaseInfo(
                tag,
                GetString(release, "name") is { Length: > 0 } name ? name : tag,
                GetString(release, "body") ?? string.Empty,
                GetString(release, "html_url") ?? $"https://github.com/{Repository}/releases",
                asset.Value.Url,
                asset.Value.Size,
                GetString(release, "published_at") ?? string.Empty);
        }
        return best;
    }

    private static (string Url, long Size)? FindAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrEmpty(url))
                continue;
            return (url, asset.GetProperty("size").GetInt64());
        }
        return null;
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary> The running exe; updates replace this file. </summary>
    private static string ExePath => Environment.ProcessPath ?? Path.Combine(Program.WorkingDirectory, AssetName);

    /// <summary>
    /// Downloads the release's exe and puts it in place of the running one (renamed to "*.old.exe", removed on the next
    /// start). After this returns, restarting the program starts the new version.
    /// </summary>
    /// <param name="release">Release to install.</param>
    /// <param name="progress">Bytes received and total.</param>
    /// <param name="token">Cancels the download.</param>
    public static async Task InstallAsync(ReleaseInfo release, IProgress<(long Received, long Total)> progress, CancellationToken token)
    {
        var exe = ExePath;
        var folder = Path.GetDirectoryName(exe)!;
        var download = exe + DownloadSuffix;
        try
        {
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.Size;
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long received = 0, reported = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    received += read;
                    if (received - reported >= 512 * 1024 || received == total)
                    {
                        reported = received;
                        progress.Report((received, total));
                    }
                }
            }

            CheckDownload(download, release.Size);
            Swap(exe, download, folder);
        }
        catch
        {
            TryDelete(download);
            throw;
        }
    }

    /// <summary> The whole file arrived and it is a Windows program. </summary>
    private static void CheckDownload(string path, long expected)
    {
        var length = new FileInfo(path).Length;
        if (expected > 0 && length != expected)
            throw new IOException($"The download is incomplete ({length:N0} of {expected:N0} bytes). Try again.");
        Span<byte> header = stackalloc byte[2];
        using var file = File.OpenRead(path);
        if (file.Read(header) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
            throw new IOException("The downloaded file isn't a Windows program.");
    }

    private static void Swap(string exe, string download, string folder)
    {
        var old = Path.Combine(folder, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
        if (File.Exists(old) && !TryDelete(old))
            old = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(exe)}.old-{DateTime.Now:yyyyMMddHHmmss}.exe");

        File.Move(exe, old); // allowed while it runs
        try
        {
            File.Move(download, exe);
        }
        catch
        {
            File.Move(old, exe); // put the running version back
            throw;
        }
    }

    /// <summary> Startup: removes what a previous update left behind (the replaced exe, an unfinished download). </summary>
    public static void CleanUp()
    {
        try
        {
            var exe = ExePath;
            var folder = Path.GetDirectoryName(exe);
            if (folder is null)
                return;
            var name = Path.GetFileNameWithoutExtension(exe);
            foreach (var file in Directory.EnumerateFiles(folder, name + ".old*.exe"))
                TryDelete(file);
            TryDelete(exe + DownloadSuffix);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal update cleanup: {ex.Message}");
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            return false; // e.g. the old exe is still closing; next start tries again
        }
    }
}

/// <summary> A TidalHeX release on GitHub. </summary>
/// <param name="Version">The release's tag, e.g. "v0.5.1-beta".</param>
/// <param name="Name">Its title.</param>
/// <param name="Notes">Its description (Markdown).</param>
/// <param name="PageUrl">The release page.</param>
/// <param name="AssetUrl">Download address of the exe.</param>
/// <param name="Size">Size of the exe in bytes.</param>
/// <param name="Published">Publish time (ISO 8601).</param>
internal sealed record ReleaseInfo(string Version, string Name, string Notes, string PageUrl, string AssetUrl, long Size, string Published);
