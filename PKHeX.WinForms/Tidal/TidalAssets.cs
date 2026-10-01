using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// TidalHeX branding assets (embedded in the executable).
/// </summary>
public static class TidalAssets
{
    private static Image? _logo;
    private static Icon? _icon;

    /// <summary> Full-resolution (512px) logo with transparency. </summary>
    public static Image Logo => _logo ??= Load(s => Image.FromStream(s), "TidalHeX.logo.png") ?? new Bitmap(1, 1);

    /// <summary> Multi-size application icon, used for every window. </summary>
    public static Icon AppIcon => _icon ??= Load(s => new Icon(s), "TidalHeX.icon.ico") ?? Properties.Resources.Icon;

    private static T? Load<T>(Func<Stream, T> factory, string name) where T : class
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream is null)
                return null;
            // Copy so the stream can be disposed (GDI+ images require their source stream to stay alive).
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return factory(copy);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// High-quality downscale of the logo, for crisp small renders (nav rail, splash).
    /// Decodes a private copy, so it is safe to call from the splash screen's thread.
    /// </summary>
    public static Bitmap GetLogo(int size)
    {
        var bmp = new Bitmap(size, size);
        using var source = Load(s => Image.FromStream(s), "TidalHeX.logo.png");
        if (source is null)
            return bmp;
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(source, 0, 0, size, size);
        return bmp;
    }
}
