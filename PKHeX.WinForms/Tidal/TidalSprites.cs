using System.Drawing;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Sprite helpers for the Tidal browsers.
/// </summary>
public static class TidalSprites
{
    /// <summary>
    /// Gets an encounter sprite without the square encounter-type background color;
    /// the Tidal cards show the encounter type as a badge instead. Must be called on the UI thread.
    /// </summary>
    public static Image GetClean(IEncounterTemplate enc)
    {
        var previous = SpriteBuilder.ShowEncounterColor;
        SpriteBuilder.ShowEncounterColor = SpriteBackgroundType.None;
        try
        {
            if (enc is { Context: EntityContext.Gen3, Species: (ushort)Species.Deoxys })
                return GetDeoxys3(enc);
            return enc.Sprite();
        }
        finally
        {
            SpriteBuilder.ShowEncounterColor = previous;
        }
    }

    /// <summary>
    /// Gen 3 Deoxys in the forme of the encounter's own game. The sprite engine draws Gen 3 Deoxys in the loaded save's
    /// forme (in FireRed every Deoxys is Attack Forme), which would make "Deoxys-Defense" look like Attack Forme.
    /// </summary>
    private static Image GetDeoxys3(IEncounterTemplate enc)
    {
        var shiny = enc.IsShiny ? Shiny.Always : Shiny.Never;
        var img = SpriteUtil.GetSprite(enc.Species, enc.Form, SpriteUtil.GetDisplayGender(enc), 0, 0, enc.IsEgg, shiny, EntityContext.None);
        if (SpriteBuilder.ShowEncounterBall && enc.FixedBall != Ball.None)
        {
            var ball = SpriteUtil.GetBallSprite((byte)enc.FixedBall);
            img = Drawing.ImageUtil.LayerImage(img, ball, 0, img.Height - ball.Height);
        }
        return img;
    }
}
