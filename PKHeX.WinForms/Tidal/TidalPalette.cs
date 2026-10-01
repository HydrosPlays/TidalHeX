using System.Drawing;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Color tokens for the Tidal theme: an ocean-blue, console-dashboard inspired look.
/// </summary>
public static class TidalPalette
{
    // Window backdrop (outer "water")
    public static readonly Color BackdropTop = Color.FromArgb(14, 132, 204);
    public static readonly Color BackdropBottom = Color.FromArgb(8, 58, 140);
    public static readonly Color BackdropGlow = Color.FromArgb(46, 214, 250);

    // Content panels (the "screens" that sit on the water)
    public static readonly Color PanelTop = Color.FromArgb(19, 112, 188);
    public static readonly Color PanelBottom = Color.FromArgb(10, 70, 146);
    public static readonly Color PanelEdge = Color.FromArgb(120, 226, 255);

    // Inputs and lists
    public static readonly Color Field = Color.FromArgb(9, 42, 84);
    public static readonly Color FieldAlt = Color.FromArgb(13, 52, 100);
    public static readonly Color FieldBorder = Color.FromArgb(63, 200, 240);

    // Buttons
    public static readonly Color Button = Color.FromArgb(12, 56, 108);
    public static readonly Color ButtonHover = Color.FromArgb(24, 96, 164);
    public static readonly Color ButtonPressed = Color.FromArgb(8, 38, 76);
    public static readonly Color ButtonBorder = Color.FromArgb(82, 210, 245);

    // Accents
    public static readonly Color Accent = Color.FromArgb(63, 224, 255);
    public static readonly Color AccentSoft = Color.FromArgb(150, 236, 255);
    public static readonly Color Selection = Color.FromArgb(10, 36, 66);
    public static readonly Color Strip = Color.FromArgb(8, 48, 98);

    // Menus
    public static readonly Color Menu = Color.FromArgb(9, 50, 100);
    public static readonly Color MenuHover = Color.FromArgb(24, 104, 176);
    public static readonly Color MenuBorder = Color.FromArgb(63, 200, 240);

    // Text
    public static readonly Color Text = Color.White;
    public static readonly Color TextSoft = Color.FromArgb(190, 232, 255);
    public static readonly Color TextMuted = Color.FromArgb(140, 190, 225);

    // Encounter type badges
    public static readonly Color BadgeWild = Color.FromArgb(46, 170, 90);
    public static readonly Color BadgeStatic = Color.FromArgb(200, 120, 30);
    public static readonly Color BadgeTrade = Color.FromArgb(150, 80, 200);
    public static readonly Color BadgeEgg = Color.FromArgb(220, 170, 40);
    public static readonly Color BadgeGift = Color.FromArgb(220, 60, 110);
    public static readonly Color BadgeOther = Color.FromArgb(70, 110, 160);
}
