using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

// trainer.*: the fields every game shares (name, gender, IDs, money, play time) plus coins and badges where the
// save has them, following SAV_SimpleTrainer. Game-specific details stay in the classic editor ("More options").
internal sealed partial class WebApi
{
    private void RegisterTrainerTool()
    {
        Bridge.Register("trainer.get", _ => GetTrainer());
        Bridge.Register("trainer.save", c => SaveTrainer(c.Get<TrainerSaveArgs>()));
    }

    /// <summary> Gen 1/2 saves only have a TID (the ID-format helper only knows that for Pokémon, not saves). </summary>
    private static TrainerIDFormat GetIdFormat(SaveFile sav) => sav.Generation <= 2 ? TrainerIDFormat.SixteenBitSingle : sav.GetTrainerIDFormat();

    private TrainerStateDto GetTrainer()
    {
        try
        {
            return ReadTrainer();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException("This save's trainer data can't be read (the save may be blank or incomplete).", ex);
        }
    }

    private TrainerStateDto ReadTrainer()
    {
        var sav = SAV;
        var format = GetIdFormat(sav);
        var badges = GetBadgeLayout(sav);
        var value = GetBadgeValue(sav);
        return new TrainerStateDto
        {
            Game = GetGameName(sav),
            Ot = sav.OT,
            OtMaxLength = sav.MaxStringLengthTrainer,
            Gender = sav.Generation > 1 ? sav.Gender : null,
            IdFormat = format switch { TrainerIDFormat.SixDigit => "six", TrainerIDFormat.SixteenBitSingle => "single", _ => "sixteen" },
            Tid = sav.GetDisplayTID(),
            Sid = sav.GetDisplaySID(),
            Money = sav.Money,
            MaxMoney = sav.MaxMoney,
            Hours = sav.PlayedHours,
            Minutes = sav.PlayedMinutes,
            Seconds = sav.PlayedSeconds,
            MaxHours = sav.Generation <= 2 ? byte.MaxValue : ushort.MaxValue,
            Coins = sav switch { SAV1 s1 => (int)s1.Coin, SAV2 s2 => (int)s2.Coin, _ => null },
            MaxCoins = sav.MaxCoins,
            BadgeGroups = badges,
            Badges = GetBadgeFlags(value, badges),
        };
    }

    private static string GetGameName(SaveFile sav)
    {
        var names = GameInfo.Strings.gamelist;
        return (uint)sav.Version < names.Length && !string.IsNullOrEmpty(names[(int)sav.Version]) ? names[(int)sav.Version] : sav.Version.ToString();
    }

    /// <summary> SAV_SimpleTrainer.B_Save_Click (the shared fields). </summary>
    private bool SaveTrainer(TrainerSaveArgs a)
    {
        var sav = SAV;
        var ot = a.Ot ?? string.Empty;
        if (ot.Length > sav.MaxStringLengthTrainer)
            ot = ot[..sav.MaxStringLengthTrainer];
        if (sav.OT != ot) // only when changed: keeps the name's trash bytes otherwise
            sav.OT = ot;
        if (sav.Generation > 1 && a.Gender is 0 or 1)
            sav.Gender = (byte)a.Gender;

        var format = GetIdFormat(sav);
        uint tid = format == TrainerIDFormat.SixDigit ? Math.Min(a.Tid, 999_999) : Math.Min(a.Tid, ushort.MaxValue);
        uint sid = format == TrainerIDFormat.SixDigit ? Math.Min(a.Sid, 4294) : Math.Min(a.Sid, ushort.MaxValue);
        if (format == TrainerIDFormat.SixDigit && !sav.IsValidTrainerID7(sid, tid))
            throw new ArgumentException("That TID/SID combination doesn't fit in the save (SID too high for this TID).");
        if (format == TrainerIDFormat.SixteenBitSingle)
            sav.TID16 = (ushort)tid; // Gen 1/2 have no SID
        else
            sav.SetDisplayID(tid, sid);

        sav.Money = (uint)Math.Min(a.Money, (uint)sav.MaxMoney);
        sav.PlayedHours = (int)Math.Min(a.Hours, ushort.MaxValue);
        sav.PlayedMinutes = (int)(a.Minutes % 60);
        sav.PlayedSeconds = (int)(a.Seconds % 60);

        if (a.Coins is { } coins)
        {
            var c = (ushort)Math.Min(coins, (uint)sav.MaxCoins);
            if (sav is SAV1 s1) s1.Coin = c;
            else if (sav is SAV2 s2) s2.Coin = c;
        }

        var layout = GetBadgeLayout(sav);
        if (layout.Count != 0 && a.Badges is { } flags)
            SetBadgeValue(sav, GetBadgeValue(flags, layout));

        sav.State.Edited = true;
        EmitSaveChanged();
        return true;
    }

    #region Badges

    private static readonly string[] Kanto = ["Boulder", "Cascade", "Thunder", "Rainbow", "Soul", "Marsh", "Volcano", "Earth"];
    private static readonly string[] Johto = ["Zephyr", "Hive", "Plain", "Fog", "Storm", "Mineral", "Glacier", "Rising"];
    private static readonly string[] Hoenn = ["Stone", "Knuckle", "Dynamo", "Heat", "Balance", "Feather", "Mind", "Rain"];
    private static readonly string[] Sinnoh = ["Coal", "Forest", "Cobble", "Fen", "Relic", "Mine", "Icicle", "Beacon"];
    private static readonly string[] UnovaBW = ["Trio", "Basic", "Insect", "Bolt", "Quake", "Jet", "Freeze", "Legend"];
    private static readonly string[] UnovaB2W2 = ["Basic", "Toxic", "Insect", "Bolt", "Quake", "Jet", "Legend", "Wave"];
    private static readonly string[] Kalos = ["Bug", "Cliff", "Rumble", "Plant", "Voltage", "Fairy", "Psychic", "Iceberg"];
    private static readonly string[] Galar = ["Grass", "Water", "Fire", "Fighting/Ghost", "Fairy", "Rock/Ice", "Dark/Dragon", "Dragon"];

    /// <summary>
    /// Badge groups in bit order. Gold/Silver/Crystal store Storm (5th) and Mineral (6th) swapped, as SAV_SimpleTrainer maps.
    /// </summary>
    private static List<BadgeGroupDto> GetBadgeLayout(SaveFile sav) => sav switch
    {
        SAV1 => [new("Kanto", Kanto)],
        SAV2 => [new("Johto", ["Zephyr", "Hive", "Plain", "Fog", "Mineral", "Storm", "Glacier", "Rising"]), new("Kanto", Kanto)],
        SAV3FRLG => [new("Kanto", Kanto)],
        SAV3 => [new("Hoenn", Hoenn)],
        SAV4HGSS => [new("Johto", Johto), new("Kanto", Kanto)],
        SAV4 => [new("Sinnoh", Sinnoh)],
        SAV5B2W2 => [new("Unova", UnovaB2W2)],
        SAV5 => [new("Unova", UnovaBW)],
        SAV6XY => [new("Kalos", Kalos)],
        SAV6AO => [new("Hoenn", Hoenn)],
        SAV8SWSH => [new("Galar", Galar)],
        _ => [],
    };

    private static int GetBadgeValue(SaveFile sav) => sav switch
    {
        SAV1 s => s.Badges,
        SAV2 s => s.Badges,
        SAV3 s => s.Badges,
        SAV4HGSS s => s.Badges | (s.Badges16 << 8),
        SAV4 s => s.Badges,
        SAV5 s => s.Misc.Badges,
        SAV6 s => s.Badges,
        SAV8SWSH s => s.Badges,
        _ => 0,
    };

    private static void SetBadgeValue(SaveFile sav, int value)
    {
        switch (sav)
        {
            case SAV1 s: s.Badges = value & 0xFF; break;
            case SAV2 s: s.Badges = value & 0xFFFF; break;
            case SAV3 s: s.Badges = value & 0xFF; break;
            case SAV4HGSS s: s.Badges = (byte)value; s.Badges16 = value >> 8; break;
            case SAV4 s: s.Badges = (byte)value; break;
            case SAV5 s: s.Misc.Badges = value & 0xFF; break;
            case SAV6 s: s.Badges = value & 0xFF; break;
            case SAV8SWSH s: s.Badges = value & 0xFF; break;
        }
    }

    private static bool[] GetBadgeFlags(int value, List<BadgeGroupDto> layout)
    {
        int count = 0;
        foreach (var g in layout)
            count += g.Names.Count;
        var flags = new bool[count];
        for (int i = 0; i < count; i++)
            flags[i] = (value & (1 << i)) != 0;
        return flags;
    }

    private static int GetBadgeValue(IReadOnlyList<bool> flags, List<BadgeGroupDto> layout)
    {
        int count = 0;
        foreach (var g in layout)
            count += g.Names.Count;
        int value = 0;
        for (int i = 0; i < Math.Min(count, flags.Count); i++)
        {
            if (flags[i])
                value |= 1 << i;
        }
        return value;
    }

    #endregion
}
