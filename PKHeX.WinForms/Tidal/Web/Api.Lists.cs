using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

// list.get: combo sources filtered for the loaded save (GameInfo.FilteredSources), in the program language.
internal sealed partial class WebApi
{
    private void RegisterLists() => Bridge.Register("list.get", c =>
    {
        var args = c.Get<ListArgs>();
        return args.Name switch
        {
            "metLocations" => GetLocations(args.Version, egg: false),
            "eggLocations" => GetLocations(args.Version, egg: true),
            _ => GetList(args.Name),
        };
    });

    /// <summary> Met/egg locations for an origin game, with PKHeX's fallback when the game has no location group. </summary>
    private List<ListItem> GetLocations(int? version, bool egg)
    {
        var pk = Session.Editor;
        var v = (GameVersion)(version ?? (int)pk.Version);
        if (v == GameVersion.Any || GameUtil.GetMetLocationVersionGroup(v) == GameVersion.Invalid)
        {
            var group = GameUtil.GetMetLocationVersionGroup(SAV.Version);
            v = group is GameVersion.Invalid or GameVersion.Any ? pk.Context.GetSingleGameVersion() : v == GameVersion.Any ? SAV.Version : v;
        }
        return GameInfo.GetLocationList(v, pk.Context, egg).Select(z => new ListItem(z.Value, z.Text)).ToList();
    }

    private static List<ListItem> GetList(string name)
    {
        var source = GameInfo.FilteredSources;
        IReadOnlyList<ComboItem> items = name.ToLowerInvariant() switch
        {
            "species" => source.Species,
            "moves" => source.Moves,
            "items" => source.Items,
            "natures" => source.Natures,
            "abilities" => source.Abilities,
            "balls" => source.Balls,
            "languages" => source.Languages,
            "games" => source.Games,
            "types" => GetTypes(),
            _ => throw new ArgumentException($"Unknown list '{name}'."),
        };
        return items.Select(z => new ListItem(z.Value, z.Text)).ToList();
    }

    private static ComboItem[] GetTypes()
    {
        var types = GameInfo.Strings.types;
        var result = new ComboItem[types.Length];
        for (int i = 0; i < types.Length; i++)
            result[i] = new ComboItem(types[i], i);
        return result;
    }
}
