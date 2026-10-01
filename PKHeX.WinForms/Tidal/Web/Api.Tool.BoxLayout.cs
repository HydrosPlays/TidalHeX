using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

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

    /// <summary> Wallpaper choices by generation (SAV_BoxLayout.LoadWallpapers); empty if the game has none. </summary>
    private static string[] GetWallpaperNames(SaveFile sav)
    {
        if (sav is not IBoxDetailWallpaper)
            return [];
        var names = GameInfo.Strings.wallpapernames;
        static string[] Placeholders(int count) => Enumerable.Range(1, count).Select(i => $"Wallpaper {i}").ToArray();
        return sav.Generation switch
        {
            3 when sav is SAV3 or SAV3RSBox => names[..16],
            4 or 5 or 6 => names[..24],
            7 => names[..16],
            8 when sav is SAV8BS => names[..32],
            8 => Placeholders(19),
            9 => Placeholders(20),
            _ => [],
        };
    }

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
