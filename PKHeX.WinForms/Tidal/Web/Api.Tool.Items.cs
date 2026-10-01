using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PKHeX.Core;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// items.*: the bag editor, a port of SAV_Inventory (same load sanity checks, save rules and pouch actions).
internal sealed partial class WebApi
{
    private void RegisterItemsTool()
    {
        Bridge.Register("items.get", _ => GetBag());
        Bridge.Register("items.save", c => SaveBag(c.Get<BagSaveArgs>()));
        Bridge.Register("items.action", c => RunBagAction(c.Get<BagActionArgs>()));
    }

    /// <summary> Item names for the save's game; blank names become "(Item #123)" like the classic editor. </summary>
    private string[] GetItemNames()
    {
        var names = GameInfo.Strings.GetItemStrings(SAV.Context, SAV.Version).ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrEmpty(names[i]))
                names[i] = $"(Item #{i:000})";
        }
        return names;
    }

    /// <summary> SAV_Inventory ctor + LoadAllBags. </summary>
    private BagState GetBag()
    {
        var sav = SAV;
        PlayerBag bag;
        try
        {
            bag = sav.Inventory; // a fresh copy: nothing reaches the save until items.save
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            // e.g. a blank Legends: Arceus save has no satchel size yet
            throw new InvalidOperationException("This save's bag can't be read (the save may be blank or incomplete).", ex);
        }
        var names = GetItemNames();
        if (bag.Pouches.Count == 0)
            return new BagState { ItemNames = names };

        var item0 = bag.Pouches[0].Items[0];
        var columns = new BagColumns(item0 is IItemFavorite, item0 is IItemNewFlag, item0 is IItemFreeSpace,
            item0 is IItemFreeSpaceIndex, item0 is IItemNewShopFlag, item0 is IItemHeldFlag);

        var pouches = new List<BagPouchDto>(bag.Pouches.Count);
        foreach (var pouch in bag.Pouches)
        {
            // Sanity screen (LoadAllBags): unknown IDs, and items that don't belong in this pouch.
            var invalid = Array.FindAll(pouch.Items, z => z.Index != 0 && !pouch.CanContain((ushort)z.Index));
            var outOfBounds = Array.FindAll(invalid, z => z.Index >= names.Length);
            var incorrectPouch = Array.FindAll(invalid, z => z.Index < names.Length);
            if (outOfBounds.Length != 0)
                WinFormsUtil.Error(MsgItemPouchUnknown, $"Item ID(s): {string.Join(", ", outOfBounds.Select(z => z.Index))}");
            if (!HaX && incorrectPouch.Length != 0)
                WinFormsUtil.Alert(string.Format(MsgItemPouchRemoved, pouch.Type), string.Join(", ", incorrectPouch.Select(z => names[z.Index])), MsgItemPouchWarning);

            pouch.Sanitize(names.Length - 1, HaX);
            pouches.Add(ToDto(bag, pouch));
        }

        return new BagState
        {
            Pouches = pouches,
            ItemNames = names,
            Columns = columns,
            ItemsFixed = sav is SAV9ZA or SAV9SV, // rows are fixed per item there; only the other fields change
            Hax = HaX,
            MaxQuantityHaX = bag.MaxQuantityHaX,
        };
    }

    private BagPouchDto ToDto(PlayerBag bag, InventoryPouch pouch)
    {
        var valid = pouch.GetAllItems();
        var items = new List<BagItemDto>(pouch.Items.Length);
        foreach (var raw in pouch.Items)
        {
            var item = raw.Index != 0 && !HaX && !valid.Contains((ushort)raw.Index) ? pouch.GetEmpty() : raw; // GetBag
            if (item.Index == 0)
                continue; // empty slots: the page adds into free slots; saving compacts them anyway
            items.Add(new BagItemDto
            {
                Id = item.Index,
                Count = item.Count,
                Favorite = item is IItemFavorite { IsFavorite: true },
                New = item is IItemNewFlag { IsNew: true },
                Free = item is IItemFreeSpace { IsFreeSpace: true },
                FreeIndex = item is IItemFreeSpaceIndex fi ? fi.FreeSpaceIndex : 0,
                Shop = item is IItemNewShopFlag { IsNewShop: true },
                Held = item is IItemHeldFlag { IsHeld: true },
            });
        }

        // ChangeViewedPouch: PC items / free space can't be bulk-filled (except Legends: Arceus).
        bool noBulk = pouch.Type is InventoryType.PCItems or InventoryType.FreeSpace && SAV is not SAV8LA;
        return new BagPouchDto
        {
            Type = pouch.Type.ToString(),
            Name = GetPouchName(pouch.Type),
            Capacity = pouch.Items.Length,
            MaxCount = pouch.MaxCount,
            DefaultCount = Math.Max(1, pouch.MaxCount - 4),
            CanBulkGive = !noBulk || HaX,
            Cramped = pouch.IsCramped,
            Allowed = HaX ? [] : valid.ToArray().Select(z => (int)z).ToArray(), // HaX: any item
            Items = items,
        };
    }

    private static string GetPouchName(InventoryType type) => type switch
    {
        InventoryType.Items => "Items",
        InventoryType.KeyItems => "Key Items",
        InventoryType.TMHMs => "TMs & HMs",
        InventoryType.Medicine => "Medicine",
        InventoryType.Berries => "Berries",
        InventoryType.Balls => "Poké Balls",
        InventoryType.BattleItems => "Battle Items",
        InventoryType.MailItems => "Mail",
        InventoryType.PCItems => "PC Items",
        InventoryType.FreeSpace => "Free Space",
        InventoryType.ZCrystals => "Z-Crystals",
        InventoryType.Candy => "Candy",
        InventoryType.Treasure => "Treasures",
        InventoryType.Ingredients => "Ingredients",
        InventoryType.MegaStones => "Mega Stones",
        _ => type.ToString(),
    };

    /// <summary> SetBag: writes the page's rows into <paramref name="pouch"/> with the classic quantity rules. </summary>
    private static void ApplyRows(PlayerBag bag, InventoryPouch pouch, IReadOnlyList<BagItemDto> rows, bool hasNew)
    {
        int ctr = 0;
        foreach (var row in rows)
        {
            if (ctr >= pouch.Items.Length)
                break;
            var itemID = row.Id;
            if (itemID <= 0 && !hasNew) // compression of empty slots
                continue;
            var count = row.Count;
            if (!bag.IsQuantitySane(pouch.Type, itemID, ref count, hasNew, HaX))
                continue; // ignore item

            var item = pouch.GetEmpty(itemID, count); // clean item data when saving
            if (item is IItemFreeSpace f)
                f.IsFreeSpace = row.Free;
            if (item is IItemFreeSpaceIndex fi)
                fi.FreeSpaceIndex = row.FreeIndex;
            if (item is IItemFavorite v)
                v.IsFavorite = row.Favorite;
            if (item is IItemNewFlag n)
                n.IsNew = row.New;
            if (item is IItemNewShopFlag ns)
                ns.IsNewShop = row.Shop;
            if (item is IItemHeldFlag g)
                g.IsHeld = row.Held;
            pouch.Items[ctr++] = item;
        }
        for (int i = ctr; i < pouch.Items.Length; i++)
            pouch.Items[i] = pouch.GetEmpty(); // empty slots at the end
    }

    private static bool HasNewFlag(PlayerBag bag) => bag.Pouches.Count != 0 && bag.Pouches[0].Items[0] is IItemNewFlag;

    /// <summary> B_Save_Click: SetBags + Bag.CopyTo(save). </summary>
    private bool SaveBag(BagSaveArgs args)
    {
        var sav = SAV;
        var bag = sav.Inventory;
        var hasNew = HasNewFlag(bag);
        foreach (var pouch in bag.Pouches)
        {
            var rows = args.Pouches.FirstOrDefault(p => p.Type == pouch.Type.ToString())?.Items;
            if (rows is null)
                continue; // untouched pouch keeps its data
            ApplyRows(bag, pouch, rows, hasNew);
        }
        bag.CopyTo(sav);
        sav.State.Edited = true;
        EmitSaveChanged();
        return true;
    }

    /// <summary>
    /// The pouch menu actions (give all, set all counts, remove all, sorts), run on a copy of the pouch built from the
    /// page's rows (ModifyPouch). Returns the pouch's new rows; the save is only written by items.save.
    /// </summary>
    private BagPouchDto RunBagAction(BagActionArgs args)
    {
        var bag = SAV.Inventory;
        var pouch = bag.Pouches.First(p => p.Type.ToString() == args.Pouch);
        ApplyRows(bag, pouch, args.Items, HasNewFlag(bag));

        var names = GetItemNames();
        switch (args.Action)
        {
            case "giveAll":
            {
                var items = pouch.GetAllItems().ToArray();
                if (pouch.IsCramped) // GetModifySettings: not everything fits; No = random selection
                {
                    var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgItemPouchSizeSmall, string.Format(MsgItemPouchRandom, Environment.NewLine));
                    if (dr == DialogResult.No)
                        Util.Rand.Shuffle(items);
                }
                pouch.GiveAllItems(bag, items, args.Count);
                break;
            }
            case "setCounts": pouch.ModifyAllCount(bag, args.Count); break;
            case "removeAll": pouch.RemoveAll(); break;
            case "sortName": pouch.SortByName(names); break;
            case "sortNameDesc": pouch.SortByName(names, reverse: true); break;
            case "sortCount": pouch.SortByCount(); break;
            case "sortCountDesc": pouch.SortByCount(reverse: true); break;
            case "sortIndex": pouch.SortByIndex(); break;
            case "sortIndexDesc": pouch.SortByIndex(reverse: true); break;
            default: throw new ArgumentException($"Unknown action '{args.Action}'.");
        }
        return ToDto(bag, pouch);
    }
}
