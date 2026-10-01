using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

// dex.*: seen/caught per species with PKHeX's own dex operations (entry complete/clear, seen/caught/complete all).
// Forms, languages and other per-game details stay in the classic editors ("More options").
internal sealed partial class WebApi
{
    private void RegisterPokedexTool()
    {
        Bridge.Register("dex.get", _ => GetDex());
        Bridge.Register("dex.save", c => SaveDex(c.Get<DexSaveArgs>()));
    }

    /// <summary> The operations one game's Pokédex supports, bound to that save. </summary>
    private sealed record DexOps(
        Func<ushort, bool> Seen,
        Func<ushort, bool> Caught,
        Action<ushort> MarkSeen,
        Action<ushort> MarkCaught,
        Action<ushort> Clear,
        Action<bool> SeenAll,
        Action<bool> CaughtAll,
        Action<bool> CompleteAll,
        Action ClearAll,
        bool Detailed,
        bool SeenOnly = true);

    /// <summary> Null when the game's Pokédex needs its own editor (Legends: Arceus research) or there is none. </summary>
    private static DexOps? GetDexOps(SaveFile sav) => sav switch
    {
        _ when !sav.HasPokeDex => null,
        SAV8LA => null,
        SAV4 s => From(s.Dex, sav, sp => SeenGen4(s.Dex, sav, sp)),
        SAV5 s => Gen5(s.Zukan, sav),
        SAV6XY s => Gen6(s.Zukan, sav),
        SAV6AO s => Gen6(s.Zukan, sav),
        SAV7 s => From(s.Zukan, sav, sp => SeenGen7(s.Zukan, sav, sp)),
        SAV7b s => From(s.Zukan, sav, sp => SeenGen7(s.Zukan, sav, sp)),
        SAV8SWSH s => From(s.Blocks.Zukan, sav, sp => SeenSWSH(s.Blocks.Zukan, sav, sp)),
        SAV8BS s => From(s.Zukan, sav, sp => SeenBDSP(s.Zukan, sav, sp)),
        SAV9SV s => From(s.Blocks.Zukan, sav, null), // per-form flags: entries are registered whole (caught) or cleared
        SAV9ZA s => From(s.Blocks.Zukan, sav, null),
        SAV1 or SAV2 or SAV3 => Simple(sav),
        _ => null,
    };

    private static DexOps From<T>(ZukanBase<T> z, SaveFile sav, Action<ushort>? markSeen) where T : SaveFile => new(
        z.GetSeen, z.GetCaught,
        markSeen ?? (s => z.SetDexEntryAll(s)),
        s => z.SetDexEntryAll(s),
        z.ClearDexEntryAll,
        shiny => z.SeenAll(shiny),
        shiny => z.CaughtAll(shiny),
        shiny => z.CompleteDex(shiny),
        () => { z.CaughtNone(); z.SeenNone(); },
        Detailed: true,
        SeenOnly: markSeen is not null);

    private static byte SeenGender(SaveFile sav, ushort species) => (byte)(sav.Personal[species].OnlyFemale ? 1 : 0);

    /// <summary> Gen 4: seen flag plus the first seen gender. </summary>
    private static void SeenGen4(Zukan4 z, SaveFile sav, ushort species)
    {
        z.SetSeenGender(species, SeenGender(sav, species));
        z.SetSeen(species, true);
    }

    /// <summary> Gen 7 / Let's Go: seen in one gender region and displayed (Zukan.SetSeen(species) only checks the flags). </summary>
    private static void SeenGen7(Zukan7 z, SaveFile sav, ushort species)
    {
        int region = SeenGender(sav, species);
        z.SetSeen(species, region, true);
        int bit = species - 1;
        for (int r = 0; r < 4; r++)
        {
            if (z.GetDisplayed(bit, r))
                return;
        }
        z.SetDisplayed(bit, region, true);
    }

    /// <summary> Sword/Shield: seen in one gender region of the species' dex entry. </summary>
    private static void SeenSWSH(Zukan8 z, SaveFile sav, ushort species)
    {
        if (z.GetEntry(species, out var entry))
            z.SetSeenRegion(entry, 0, SeenGender(sav, species));
    }

    /// <summary> Brilliant Diamond/Shining Pearl: "seen" state with a seen gender. </summary>
    private static void SeenBDSP(Zukan8b z, SaveFile sav, ushort species)
    {
        if (z.GetState(species) < ZukanState8b.Seen)
            z.SetState(species, ZukanState8b.Seen);
        z.GetGenderFlags(species, out var m, out var f, out var ms, out var fs);
        if (!m && !f && !ms && !fs)
        {
            if (SeenGender(sav, species) == 1) f = true;
            else m = true;
            z.SetGenderFlags(species, m, f, ms, fs);
        }
    }

    /// <summary> Builds the bulk actions from per-species ones (for dex types without their own). </summary>
    private static DexOps FromEntries(SaveFile sav, Func<ushort, bool> seen, Func<ushort, bool> caught, Action<ushort, bool> markSeen, Action<ushort, bool> markCaught, Action<ushort> clear)
    {
        void ForAll(Action<ushort> action)
        {
            for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
                action(s);
        }
        return new DexOps(
            seen, caught,
            s => markSeen(s, false),
            s => markCaught(s, false),
            clear,
            shiny => ForAll(s => markSeen(s, shiny)),
            shiny => ForAll(s => markCaught(s, shiny)),
            shiny => ForAll(s => markCaught(s, shiny)),
            () => ForAll(clear),
            Detailed: true);
    }

    /// <summary> Black/White and Black 2/White 2 (SAV_Pokedex5 semantics: seen per gender, displayed entry, caught). </summary>
    private static DexOps Gen5(Zukan5 z, SaveFile sav)
    {
        void Seen(ushort s, bool shiny)
        {
            var pi = sav.Personal[s];
            if (!pi.OnlyFemale)
            {
                z.SetSeen(s, 0);
                if (shiny) z.SetSeen(s, 2);
            }
            if (pi is { OnlyMale: false, Genderless: false })
            {
                z.SetSeen(s, 1);
                if (shiny) z.SetSeen(s, 3);
            }
            if (!z.GetDisplayedAny(s))
                z.SetDisplayed(s, pi.OnlyFemale ? 1 : 0);
        }
        return FromEntries(sav, z.GetSeen, z.GetCaught, Seen,
            (s, shiny) => { Seen(s, shiny); z.SetCaught(s); },
            s => { z.ClearSeen(s); z.ClearDisplayed(s); z.SetCaught(s, false); });
    }

    /// <summary> X/Y and Omega Ruby/Alpha Sapphire: PKHeX's Zukan6.GiveAll (genders, forms, displayed, caught, languages). </summary>
    private static DexOps Gen6(Zukan6 z, SaveFile sav)
    {
        var language = (LanguageID)sav.Language;
        return FromEntries(sav, z.GetSeen, z.GetCaught,
            (s, shiny) => z.CompleteSeen(s, shiny, sav.Personal[s]),
            (s, shiny) => z.GiveAll(s, true, shiny, language, allLanguages: true),
            s => { z.ClearSeen(s); z.ClearDisplayed(s); z.SetCaught(s, false); z.SetAllLanguage(s, false); });
    }

    /// <summary> Gen 1-3: plain seen/caught flags. </summary>
    private static DexOps Simple(SaveFile sav)
    {
        void All(bool seen, bool caught)
        {
            for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
            {
                if (seen) sav.SetSeen(s, true);
                if (caught) sav.SetCaught(s, true);
            }
        }
        return new DexOps(
            sav.GetSeen, sav.GetCaught,
            s => sav.SetSeen(s, true),
            s => { sav.SetSeen(s, true); sav.SetCaught(s, true); },
            s => { sav.SetCaught(s, false); sav.SetSeen(s, false); },
            _ => All(true, false),
            _ => All(true, true),
            _ => All(true, true),
            () => { for (ushort s = 1; s <= sav.MaxSpeciesID; s++) { sav.SetCaught(s, false); sav.SetSeen(s, false); } },
            Detailed: false);
    }

    /// <summary> Species the game's Pokédex has an entry for. </summary>
    private static bool IsInDex(SaveFile sav, ushort species) => sav switch
    {
        SAV8SWSH s => s.Blocks.Zukan.GetEntry(species, out _),
        SAV9SV s => s.Blocks.Zukan.GetDexIndex(species).Group != 0,
        SAV8BS or SAV9ZA => sav.Personal.IsSpeciesInGame(species),
        _ => true,
    };

    private DexStateDto GetDex()
    {
        var sav = SAV;
        var ops = GetDexOps(sav) ?? throw new InvalidOperationException("This game's Pokédex uses its own editor (More options).");
        var names = GameInfo.Strings.specieslist;
        var list = new List<DexEntryDto>(sav.MaxSpeciesID);
        try
        {
            for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
            {
                if (!IsInDex(sav, s))
                    continue;
                list.Add(new DexEntryDto(s, s < names.Length ? names[s] : $"#{s}", ops.Seen(s), ops.Caught(s)));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException("This save's Pokédex can't be read (the save may be blank or incomplete).", ex);
        }
        return new DexStateDto { Entries = list, Detailed = ops.Detailed, SeenOnly = ops.SeenOnly, Game = GetGameName(sav) };
    }

    /// <summary> Applies the page's actions in order (bulk actions and per-species states), then refreshes the page. </summary>
    private bool SaveDex(DexSaveArgs a)
    {
        var sav = SAV;
        var ops = GetDexOps(sav) ?? throw new InvalidOperationException("This game's Pokédex uses its own editor.");
        foreach (var op in a.Ops)
        {
            switch (op.Op)
            {
                case "seenAll": ops.SeenAll(op.Shiny); break;
                case "caughtAll": ops.CaughtAll(op.Shiny); break;
                case "completeAll": ops.CompleteAll(op.Shiny); break;
                case "clearAll": ops.ClearAll(); break;
                case "set":
                {
                    var s = (ushort)op.Species;
                    if (s == 0 || s > sav.MaxSpeciesID || !IsInDex(sav, s))
                        continue;
                    ops.Clear(s);
                    if (op.State is "seen")
                        ops.MarkSeen(s);
                    else if (op.State is "caught")
                        ops.MarkCaught(s);
                    break;
                }
                default: throw new ArgumentException($"Unknown Pokédex action '{op.Op}'.");
            }
        }
        sav.State.Edited = true;
        EmitSaveChanged();
        return true;
    }
}
