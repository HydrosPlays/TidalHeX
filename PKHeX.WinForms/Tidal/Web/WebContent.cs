using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Serves the page's static files (<c>wwwroot</c>), which are embedded in the executable as <c>tidalweb/…</c> resources.
/// </summary>
internal static class WebContent
{
    private const string ResourcePrefix = "tidalweb/";
    private const string DefaultDocument = "index.html";

    private static readonly Assembly Assembly = typeof(WebContent).Assembly;

    /// <summary> URL path (forward slashes, no leading slash) → manifest resource name. </summary>
    private static readonly Lazy<Dictionary<string, string>> Map = new(BuildMap);
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            // %(RecursiveDir) contributes backslashes; URLs use forward slashes.
            var path = name[ResourcePrefix.Length..].Replace('\\', '/');
            map[path] = name;
        }
        return map;
    }

    /// <summary>
    /// Gets an embedded file by its URL path.
    /// </summary>
    /// <param name="path">Unescaped URL path without the leading slash (e.g. <c>js/app.js</c>).</param>
    /// <param name="data">File content.</param>
    /// <param name="mime">Content type for the response.</param>
    public static bool TryGet(string path, out byte[] data, out string mime)
    {
        if (path.Length == 0 || path.EndsWith('/'))
            path += DefaultDocument;

        mime = GetMimeType(path);
        if (!Map.Value.TryGetValue(path, out var resource))
        {
            data = [];
            return false;
        }

        data = Cache.GetOrAdd(resource, Read);
        return true;
    }

    private static byte[] Read(string resource)
    {
        using var stream = Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return [];
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".wasm" => "application/wasm",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };
}
