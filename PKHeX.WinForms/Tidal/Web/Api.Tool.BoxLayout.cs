using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using static PKHeX.WinForms.WallpaperName; // "Simple" is also a method here, so that one is written in full

namespace PKHeX.WinForms.Tidal.Web;

// boxlayout.*: box names, wallpapers, order, unlocked count and flags; a port of SAV_BoxLayout
// (edits a clone of the save and copies the changes back on save, like the classic editor).
internal sealed partial class WebApi
{
    private void RegisterBoxLayoutTool()
    {
        Bridge.Register("boxlayout.get", _ => GetBoxLayout());
        Bridge.Register("boxlayout.save", c => SaveBoxLayout(c.Get<BoxLayoutSaveArgs>()));
    }

    /// <summary> Box R/S "My Wallpaper": the save's own picture, drawn as its left or right half by box position. </summary>
    internal const int PictureWallpaperRSBox = 20;

    /// <summary> Wallpaper choices by game (SAV_BoxLayout.LoadWallpapers); empty if the game has none. </summary>
    private static string[] GetWallpaperNames(SaveFile sav)
    {
        if (sav is not IBoxDetailWallpaper)
            return [];
        var names = GameInfo.Strings.wallpapernames;
        string[] Named(ReadOnlySpan<WallpaperName> list, string suffix = "")
        {
            var result = new string[list.Length];
            for (int i = 0; i < list.Length; i++)
                result[i] = names[(int)list[i]] + suffix;
            return result;
        }
        static string[] Placeholders(int count)
        {
            var prefix = GetWallpaperPrefix();
            return Enumerable.Range(1, count).Select(i => $"{prefix}{i}").ToArray();
        }

        return sav.Generation switch
        {
            3 => [..names[..12], ..Named(sav switch
            {
                SAV3RS => [PolkaDot, PokemonCenter, Machine3, Plain],
                SAV3E => [PolkaDot, PokemonCenter, Machine3, WallpaperName.Simple, Friends],
                SAV3FRLG => [Stars, PokemonCenter, Tiles, WallpaperName.Simple],
                SAV3RSBox => [PolkaDot, PokemonCenter, Machine3, Plain, Flower, Tiles, Carpet, Ruin, MyWallpaper],
                _ => [],
            })],
            4 or 5 => [..names[..16], ..Named(sav switch
            {
                SAV4DP => [Space, Backyard, Nostalgic, Torchic, Trio, Pikapika, Legend, TeamGalactic],
                SAV4Pt => [Distortion, Contest, Nostalgic, Croagunk, Trio, Pikapika, Legend, TeamGalactic],
                SAV4HGSS => [Heart, Soul, BigBrother, Pokeathlon, Trio, SpikyPika, KimonoGirl, Revival],
                SAV5BW => [Reshiram, Zekrom, Monochrome, TeamPlasma, Munna, Zoroark, Subway, Musical],
                SAV5B2W2 => [Monochrome, TeamPlasma, Movie, PWT, Kyurem1, Kyurem2, Reshiram, Zekrom],
                _ => [],
            })],
            6 => names[..24],
            7 => names[..16],
            8 when sav is SAV8BS =>
            [
                ..names[..16],
                ..Named([Space, Backyard, Nostalgic, Torchic, Trio, Pikapika, Legend, TeamGalactic]),
                ..Named([Distortion, Contest, Nostalgic, Croagunk, Trio, Pikapika, Legend, TeamGalactic], GetPlatinumSuffix()),
            ],
            8 => Placeholders(19),
            9 => Placeholders(20),
            _ => [],
        };
    }

    /// <summary> Marks BD/SP's second set of wallpapers, which reuse Diamond/Pearl names (SAV_BoxLayout.GetPlatinumSuffix). </summary>
    private static string GetPlatinumSuffix() => GameInfo.Strings.Language switch
    {
        LanguageID.Japanese => "Ｐｔ",
        LanguageID.English => " (Platinum)",
        LanguageID.German => " (Platin)",
        LanguageID.French => " (Platine)",
        LanguageID.Italian => " (Platino)",
        LanguageID.Spanish or LanguageID.SpanishL => " (Platino)",
        LanguageID.Korean => " Pt",
        LanguageID.ChineseS or LanguageID.ChineseT => "Ｐｔ",

        _ => " (Platinum)",
    };

    /// <summary> Name for the numbered wallpapers of Gen 8 and 9 (SAV_BoxLayout.GetWallpaperPrefix). </summary>
    private static string GetWallpaperPrefix() => GameInfo.Strings.Language switch
    {
        LanguageID.Japanese => "かべがみ",
        LanguageID.English => "Wallpaper ",
        LanguageID.German => "Hintergrund ",
        LanguageID.French => "Thème ",
        LanguageID.Italian => "Sfondo ",
        LanguageID.Spanish or LanguageID.SpanishL => "Fondo ",
        LanguageID.Korean => "벽지",
        LanguageID.ChineseS => "壁纸",
        LanguageID.ChineseT => "壁紙",

        _ => "Wallpaper ",
    };

    private static int GetBoxNameMaxLength(SaveFile sav) => sav.Generation switch
    {
        2 when sav is SAV2 { Japanese: false, Korean: false } => 8 * 2,
        3 when sav is SAV3RSBox => 8 + SAV3RSBox.BoxNamePrefix,
        6 or 7 => 14,
        >= 8 => 16,
        _ => 8,
    };

    private BoxLayoutDto GetBoxLayout()
    {
        var sav = SAV;
        var names = sav as IBoxDetailNameRead;
        var wallpapers = GetWallpaperNames(sav);
        var wp = sav as IBoxDetailWallpaper;
        if (names is null && wallpapers.Length == 0)
            throw new InvalidOperationException("Box layout is not supported for this game.");

        var boxes = new List<BoxLayoutBoxDto>(sav.BoxCount);
        for (int i = 0; i < sav.BoxCount; i++)
        {
            int count = 0;
            for (int s = 0; s < sav.BoxSlotCount; s++)
            {
                if ((i * sav.BoxSlotCount) + s < sav.SlotCount && sav.GetBoxSlotAtIndex(i, s).Species != 0)
                    count++;
            }
            boxes.Add(new BoxLayoutBoxDto(
                i,
                names?.GetBoxName(i) ?? $"Box {i + 1}",
                wp is null ? -1 : Math.Clamp(wp.GetBoxWallpaper(i), 0, Math.Max(0, wallpapers.Length - 1)),
                count,
                IsBoxLocked(sav, i)));
        }

        var unlocked = sav.BoxesUnlocked;
        return new BoxLayoutDto
        {
            Boxes = boxes,
            CanRename = sav is IBoxDetailName,
            NameMaxLength = GetBoxNameMaxLength(sav),
            Wallpapers = wallpapers,
            FixedWallpaper = sav is SAV9ZA or SAV8LA, // the game uses one scene for every box
            PictureWallpaper = sav is SAV3RSBox ? PictureWallpaperRSBox : null,
            Unlocked = unlocked > 0 ? Math.Min(sav.BoxCount, unlocked) : null,
            Flags = sav.BoxFlags.Select(b => (int)b).ToArray(),
            SlotsPerBox = sav.BoxSlotCount,
        };
    }

    /// <summary> Boxes with locked or team slots can't be moved (SaveFile.IsBoxAbleToMove). </summary>
    private static bool IsBoxLocked(SaveFile sav, int box)
    {
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            if (sav.GetBoxSlotFlags(box, s).IsOverwriteProtected())
                return true;
        }
        return false;
    }

    /// <summary> SAV_BoxLayout.B_Save_Click, with the reorder replayed as the same adjacent moves (MoveBox). </summary>
    private bool SaveBoxLayout(BoxLayoutSaveArgs a)
    {
        var sav = SAV;
        var clone = sav.Clone();
        int n = clone.BoxCount;
        if (a.Order.Count != n || a.Order.Distinct().Count() != n || a.Order.Any(z => (uint)z >= n))
            throw new ArgumentException("The box order doesn't match this save.");

        // Order: original box index at each new position. Bubble each box up into place with adjacent swaps.
        var current = Enumerable.Range(0, n).ToList();
        for (int target = 0; target < n; target++)
        {
            int j = current.IndexOf(a.Order[target]);
            while (j > target)
            {
                if (!clone.SwapBox(j - 1, j)) // locked/team slots
                    throw new InvalidOperationException("Locked or team slots prevent moving these boxes.");
                (current[j - 1], current[j]) = (current[j], current[j - 1]);
                j--;
            }
        }

        if (clone is IBoxDetailName names)
        {
            var max = GetBoxNameMaxLength(clone);
            for (int i = 0; i < Math.Min(n, a.Names.Count); i++)
            {
                var name = a.Names[i] ?? string.Empty;
                if (name.Length > max)
                    name = name[..max];
                if (names.GetBoxName(i) != name)
                    names.SetBoxName(i, name);
            }
        }
        if (clone is IBoxDetailWallpaper wp && a.Wallpapers.Count != 0)
        {
            var count = GetWallpaperNames(clone).Length;
            for (int i = 0; i < Math.Min(n, a.Wallpapers.Count); i++)
            {
                var w = a.Wallpapers[i];
                if ((uint)w < count && wp.GetBoxWallpaper(i) != w)
                    wp.SetBoxWallpaper(i, w);
            }
        }
        if (a.Flags is { Count: > 0 } flags && clone.BoxFlags.Length == flags.Count)
            clone.BoxFlags = flags.Select(f => (byte)Math.Clamp(f, 0, 255)).ToArray();
        if (a.Unlocked is { } unlocked && clone.BoxesUnlocked > 0)
            clone.BoxesUnlocked = Math.Clamp(unlocked, 0, n);

        sav.CopyChangesFrom(clone);
        sav.State.Edited = true;
        RefreshAllSlots(); // boxes moved: slot images, the box view and undo history change
        return true;
    }
}
