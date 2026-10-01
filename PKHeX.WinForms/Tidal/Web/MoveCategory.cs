using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Physical / special / status split of each move, which PKHeX.Core doesn't carry.
/// </summary>
internal static class MoveCategory
{
    public const char Physical = 'P';
    public const char Special = 'S';
    public const char Status = 'T';

    // One character per move ID (Move enum order), from PokeAPI's move data and Pokémon Showdown's for Nihil Light.
    private const string Table =
        "-PPPPPPPPPPPPSTPSPTPPPPPPPPPTPPPPPPPPPPTPPPTPTTTTSTSSSTSSSSSSSSSPPPPPPPSSTTPSTTTSTSSSSTSPPPPTSSTTTPP" +
        "TSTTTTTTTTTTTTTTTPTTPPPSSPSPPSPPPTTTPTSTPPTPTSPTTSTTPPPPTPPTTSPPTPTPPTTTPSTPTSTPTSTPTPTTSSSTSTTTSTPT" +
        "PTSTTPPTTPPPTTTTPPPTTPPPPSTTPPTPPPTTTSPSTTPSTPSSSPSPPSTSTSTTTTTPPPTTTTTTTTTTPTTPPTPPSTTTTTPPPTTSSTTP" +
        "TPPTSPPSSPPSTTSSTPSTTTTSSPSPPSSPPPTTTPSTPSPPPSTTPTPSSSSTTTPPPTSPPPTTPPPPPTPTSTSTTTTTTTPPTPTTTTPPSTPS" +
        "PPPSPSSPSPSSSPSTPTPPPPPPPPSPPSSPTTSSSSPPPPPPPTTSSSPSPPPTTPPSSTPSTSSPTTTTTSSTTTTPPSSTPSSTPTPSPTTTSSPS" +
        "STTSTTSPTPSTPTPSTSSSSSSPSPTSPPPPPPPPSPTSSPSPPSSSSSPSSPSSPPSPPTSTTPPTTTSTPSSTTSTTTTTPSSSTTTTPSSSSTTTT" +
        "TTTTTSTTTPPSPSPPPSSPPPPSPSPSPSPSPSPSPSPSPSPSPSPSPSPSPSPSPSPTPTPPSPTPTPPTTTTPSPTPPPSTPTSSPTPSPPTPPSSP" +
        "SPTSSSPPPPPSPPSTPSPSSPSSPSPPSPSPSSSPSSPSSPPTSSPTTTTPTTPPTPPPPPPPPPPPPPPPPPPTPTPPPPPPPPSSPPSTTPPSSSPP" +
        "SSSPSSPSPPTTPPPSTPPSSSSPPSSPPTPSSPPSPTPPSSTPSPSSSTTSTPPSPPTPPPPTPPPTTPPSPPSPSSPSTTTTPPSPSPSPPPPPPPPP" +
        "PSSSPSSSTSPSPTSPPSPSS";

    /// <summary> Category of a move in a game; before the split (Gen 1-3) a damaging move's type decided it. </summary>
    /// <returns><see cref="Physical"/>, <see cref="Special"/>, <see cref="Status"/>, or a space for none.</returns>
    public static char Get(ushort move, EntityContext context)
    {
        if (move == 0 || move >= Table.Length)
            return ' ';
        var c = Table[move];
        if (c == Status || context.Generation > 3)
            return c;
        return MoveInfo.GetType(move, context) <= (byte)MoveType.Steel ? Physical : Special;
    }

    public static string? GetName(ushort move, EntityContext context) => Get(move, context) switch
    {
        Physical => "physical",
        Special => "special",
        Status => "status",
        _ => null,
    };
}
