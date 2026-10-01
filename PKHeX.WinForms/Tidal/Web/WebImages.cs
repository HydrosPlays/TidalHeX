using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PKHeX.Core;
using PKHeX.Drawing.Misc;
using PKHeX.Drawing.PokeSprite;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Renders the image URLs of the contract (<c>/sprite/…</c>, <c>/wallpaper/…</c>) as PNG, using PKHeX's sprite builders.
/// </summary>
/// <remarks>Must run on the UI thread (the sprite builders share static state).</remarks>
internal sealed class WebImages(WebApi api)
{
    private WebSession Session => api.Session;
    private SaveFile SAV => Session.SAV;

    /// <summary>
    /// Renders an image route.
    /// </summary>
    /// <param name="path">Unescaped URL path without the leading slash, e.g. <c>sprite/slot/0/5</c>.</param>
    /// <param name="query">Query string values.</param>
    /// <param name="png">Encoded image.</param>
    /// <returns>False if the route or its arguments are unknown.</returns>
    public bool TryRender(string path, IReadOnlyDictionary<string, string> query, out byte[] png)
    {
        png = [];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var image = parts switch
        {
            ["wallpaper", "choice", var value] => GetWallpaperChoice(ToInt(value)),
            ["wallpaper", var box] => GetWallpaper(ToInt(box)),
            ["sprite", "slot", var box, var slot] => GetFlag(query, "clean") ? GetCleanSlotSprite(ToInt(box), ToInt(slot)) : GetSlotSprite(ToInt(box), ToInt(slot)),
            ["sprite", "editor"] => GetFlag(query, "clean") ? GetCleanSprite(Session.Editor, crop: false, stored: false) : Session.Editor.Sprite(SAV),
            ["sprite", "species", var species, var form] => GetSpeciesSprite(ToInt(species), ToInt(form), query),
            ["sprite", "species", var species] => GetSpeciesSprite(ToInt(species), 0, query),
            ["sprite", "enc", var token] => Session.GetResult(token) is { } enc ? TidalSprites.GetClean(enc) : null,
            ["sprite", "ball", var ball] => (uint)ToInt(ball) <= byte.MaxValue ? SpriteUtil.GetBallSprite((byte)ToInt(ball)) : null,
            ["sprite", "item", var item] => ToInt(item) is >= 0 and var id ? GetItemSprite(id) : null,
            ["sprite", "bag", var pouch] => GetPouchIcon(pouch),
            _ => null,
        };
        if (image is null)
            return false;

        try
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            png = ms.ToArray();
        }
        finally
        {
            // Some sources return images the sprite engine shares for the whole session (e.g. the empty-slot sprite
            // for species 0, TM/TR/unknown item icons); disposing those would break every later use, including classic tools.
            if (!IsShared(image))
                image.Dispose();
        }
        return true;
    }

    private static bool IsShared(Image image)
    {
        var s = SpriteUtil.Spriter;
        return ReferenceEquals(image, s.None) || ReferenceEquals(image, s.Transparent)
            || ReferenceEquals(image, s.UnknownItem) || ReferenceEquals(image, s.ItemTM) || ReferenceEquals(image, s.ItemTR)
            || ReferenceEquals(image, s.Hover) || ReferenceEquals(image, s.View) || ReferenceEquals(image, s.Set)
            || ReferenceEquals(image, s.Delete) || ReferenceEquals(image, s.Drag) || ReferenceEquals(image, s.ShadowLugia);
    }

    /// <summary> Item icon; older games' item IDs are mapped to the display ID first (SAV_Inventory.UpdateSprite). </summary>
    private Image? GetItemSprite(int id)
    {
        var context = SAV.Context;
        var display = ItemConverter.GetItemDisplay(id, context);
        return display == 0 ? GetBlankItem() : SpriteUtil.Spriter.GetItemSprite(display, context);
    }

    private static Bitmap GetBlankItem() => new(1, 1);

    /// <summary> Bag pouch icons (the classic editor's tab images). </summary>
    private static Image? GetPouchIcon(string pouch) => Enum.TryParse<InventoryType>(pouch, out var type) ? type switch
    {
        InventoryType.Items => Properties.Resources.bag_items,
        InventoryType.KeyItems => Properties.Resources.bag_key,
        InventoryType.TMHMs => Properties.Resources.bag_tech,
        InventoryType.Medicine => Properties.Resources.bag_medicine,
        InventoryType.Berries => Properties.Resources.bag_berries,
        InventoryType.Balls => Properties.Resources.bag_balls,
        InventoryType.BattleItems => Properties.Resources.bag_battle,
        InventoryType.MailItems => Properties.Resources.bag_mail,
        InventoryType.PCItems => Properties.Resources.bag_pcitems,
        InventoryType.FreeSpace => Properties.Resources.bag_free,
        InventoryType.ZCrystals => Properties.Resources.bag_z,
        InventoryType.Candy => Properties.Resources.bag_candy,
        InventoryType.Treasure => Properties.Resources.bag_treasure,
        InventoryType.Ingredients => Properties.Resources.bag_ingredient,
        InventoryType.MegaStones => Properties.Resources.bag_mega,
        _ => null,
    } : null;

    private static int ToInt(string value) => int.TryParse(value, out var result) ? result : int.MinValue;

    private static readonly System.Resources.ResourceManager WallpaperResources =
        new("PKHeX.Drawing.Misc.Properties.Resources", typeof(WallpaperUtil).Assembly);

    /// <summary> A wallpaper by its value (for choosing one before it's saved), as WallpaperUtil draws it. </summary>
    private Image? GetWallpaperChoice(int value)
    {
        var sav = SAV;
        if (value < 0 || sav is not IBoxDetailWallpaper)
            return null;
        if (sav is SAV9ZA or SAV8LA) // one fixed scene for every box
            return GetWallpaper(0);
        var name = WallpaperUtil.GetWallpaperResourceName(sav.Version, value);
        return WallpaperResources.GetObject(name) as Image ?? GetWallpaper(0);
    }

    private Image? GetWallpaper(int box)
    {
        if (!SAV.HasBox || (uint)box >= SAV.BoxCount)
            return null;
        return SAV.WallpaperImage(box);
    }

    /// <summary>
    /// Same image the classic slot shows (SlotUtil.UpdateSlot): legality flag, held item, shiny, egg, lock/team markers.
    /// </summary>
    private Image? GetSlotSprite(int box, int slot)
    {
        var sav = SAV;
        if (!TryReadSlot(box, slot, out var pk, out var type))
            return null;
        if (pk is null || pk.Species == 0)
            return GetBlank();
        if (!pk.Valid) // bad egg / corrupt data: classic shows an empty slot with a warning color
        {
            var blank = GetBlank();
            var warn = SpriteUtil.GetLegalIndicator(false);
            return Drawing.ImageUtil.LayerImage(blank, warn, 0, blank.Height - warn.Height);
        }

        var flags = api.FlagIllegal ? SlotVisibilityType.CheckLegalityIndicate : SlotVisibilityType.None;
        return pk.Sprite(sav, box, slot, flags, type);
    }

    /// <summary> Reads a slot; false if the slot doesn't exist, null for slots past the end of storage. </summary>
    private bool TryReadSlot(int box, int slot, out PKM? pk, out StorageSlotType type)
    {
        var sav = SAV;
        pk = null;
        type = StorageSlotType.None;
        if (box == SlotRef.Party)
        {
            if (!sav.HasParty || (uint)slot >= WebSession.PartySize)
                return false;
            pk = slot < Math.Min(sav.PartyCount, WebSession.PartySize) ? sav.GetPartySlotAtIndex(slot) : sav.BlankPKM;
            type = StorageSlotType.Party;
            return true;
        }
        if (!sav.HasBox || (uint)box >= sav.BoxCount || (uint)slot >= sav.BoxSlotCount)
            return false;
        if ((box * sav.BoxSlotCount) + slot >= sav.SlotCount)
            return true;
        var info = new SlotInfoBox(box, slot, sav);
        pk = info.Read(sav);
        type = info.Type;
        return true;
    }

    /// <summary>
    /// Just the Pokémon (no held item, legality or slot markers), cropped to its visible pixels: for round avatars,
    /// where the full 68×56 slot sprite would leave the Pokémon small and off-center.
    /// </summary>
    private Image? GetCleanSlotSprite(int box, int slot)
    {
        if (!TryReadSlot(box, slot, out var pk, out _))
            return null;
        return GetCleanSprite(pk, crop: true);
    }

    /// <summary>
    /// The Pokémon's art without PKHeX's slot overlays (held item, shiny sparkle, Tera Type stripe, legality mark):
    /// for large sprites (editor hero, home tile) and avatars, where the page shows those details itself.
    /// </summary>
    /// <param name="pk">Pokémon to draw.</param>
    /// <param name="crop">Trim the transparent border.</param>
    /// <param name="stored">
    /// Stored data, where a bad checksum means a corrupt slot (drawn blank). The editor's working copy isn't finalized
    /// while it's being edited, so its checksum is stale after any change and must not be checked.
    /// </param>
    private static Image GetCleanSprite(PKM? pk, bool crop, bool stored = true)
    {
        if (pk is null || pk.Species == 0 || (stored && !pk.Valid))
            return GetBlank();

        var formArg = pk is IFormArgument f ? f.FormArgument : 0;
        // Shiny colors but no sparkle; eggs keep PKHeX's egg sprite.
        var sprite = pk.IsEgg
            ? SpriteUtil.GetSprite(pk.Species, pk.Form, pk.Gender, formArg, 0, true, Shiny.Never, pk.Context)
            : SpriteUtil.Spriter.GetBaseSprite(pk.Species, pk.Form, pk.Gender, formArg, pk.IsShiny, pk.Context);
        if (!crop)
            return IsShared(sprite) ? new Bitmap(sprite) : sprite;
        try
        {
            return CropToContent(sprite);
        }
        finally
        {
            if (!IsShared(sprite))
                sprite.Dispose();
        }
    }

    /// <summary> Copy of <paramref name="image"/> trimmed to its non-transparent pixels (plus a 1px margin). </summary>
    private static Bitmap CropToContent(Bitmap image)
    {
        int w = image.Width, h = image.Height;
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        try
        {
            var pixels = new int[w * h];
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * w, w);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if ((uint)pixels[(y * w) + x] >> 24 < 8) // nearly transparent
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        if (maxX < 0)
            return new Bitmap(image);
        var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        rect.Inflate(1, 1);
        rect.Intersect(new Rectangle(0, 0, w, h));
        return image.Clone(rect, PixelFormat.Format32bppArgb);
    }

    private static Bitmap GetBlank() => new(SpriteUtil.Spriter.Width, SpriteUtil.Spriter.Height);

    private Image GetSpeciesSprite(int species, int form, IReadOnlyDictionary<string, string> query)
    {
        var s = (ushort)Math.Clamp(species, 0, ushort.MaxValue);
        var f = (byte)Math.Clamp(form, 0, byte.MaxValue);
        var shiny = GetFlag(query, "shiny") ? Shiny.Always : Shiny.Never;
        var egg = GetFlag(query, "egg");
        var gender = query.TryGetValue("gender", out var g) && byte.TryParse(g, out var value) && value <= 2 ? value : (byte)0;
        return SpriteUtil.GetSprite(s, f, gender, 0, 0, egg, shiny, SAV.Context);
    }

    private static bool GetFlag(IReadOnlyDictionary<string, string> query, string key)
        => query.TryGetValue(key, out var value) && value is "1" or "true";
}
