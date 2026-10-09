using System;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// TidalHeX's own version (PKHeX's is <see cref="Program.CurrentVersion"/>). The updater compares it with the tags of the
/// GitHub releases, so bump it before publishing a release, and tag the release with the same text ("0.5.1-beta" or
/// "v0.5.1-beta").
/// </summary>
internal static class TidalVersion
{
    public const string Current = "0.6.0-beta";

    /// <summary> Display form: "0.5.0-beta" → "0.5.0 Beta". </summary>
    public static string Display(string version)
    {
        if (!TryParse(version, out var number, out var label))
            return version;
        var text = number.ToString(3);
        return label.Length == 0 ? text : $"{text} {char.ToUpperInvariant(label[0])}{label[1..]}";
    }

    /// <summary>
    /// Reads "0.5.0-beta", "v0.5.0-beta", "0.5.0" or "v1.2" into its number and pre-release label ("beta", "" for a full release).
    /// </summary>
    public static bool TryParse(string? tag, out Version number, out string label)
    {
        number = new Version(0, 0, 0);
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            label = text[(dash + 1)..].ToLowerInvariant();
            text = text[..dash];
        }
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var parsed))
            return false;
        number = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    /// <summary>
    /// Orders two versions: by number, then a full release after its pre-releases ("1.0.0" &gt; "1.0.0-beta"), then by label.
    /// Unreadable versions sort first.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        bool okA = TryParse(a, out var na, out var la), okB = TryParse(b, out var nb, out var lb);
        if (!okA || !okB)
            return okA.CompareTo(okB);
        var byNumber = na.CompareTo(nb);
        if (byNumber != 0)
            return byNumber;
        if (la.Length == 0 || lb.Length == 0)
            return (la.Length == 0).CompareTo(lb.Length == 0);
        return string.CompareOrdinal(la, lb);
    }
}
