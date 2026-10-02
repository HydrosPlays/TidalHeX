using System;
using System.Text.Json;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// A storage slot reference; <see cref="Box"/> = -1 is the party.
/// </summary>
public sealed record SlotRef(int Box, int Slot)
{
    public const int Party = -1;
    public bool IsParty => Box == Party;
}

public sealed record BoxArgs(int Box);
public sealed record SlotArgs(SlotRef Slot);

/// <summary> Result of <c>box.dropFile</c>: the Pokémon was placed in the slot, or the file was opened normally (Save is set if a save loaded). </summary>
public sealed record SlotDropResult(bool Placed, SaveSummary? Save);
public sealed record MoveArgs(SlotRef From, SlotRef To, string? Mode);
public sealed record PathArgs(string Path);
public sealed record LibraryArgs(string? Id, bool Refresh);
public sealed record ThemeArgs(string Theme);
public sealed record NameArgs(string Name);
public sealed record ListArgs(string Name, int? Version);
public sealed record FieldArgs(string Field, System.Text.Json.JsonElement Value);
public sealed record WhatArgs(string What);
public sealed record OptionArgs(string Name, bool Value);
public sealed record DevBlankArgs(string Version);
public sealed record DialogAnswerArgs(int Id, string Result);
public sealed record DevDropArgs(SlotRef Slot, string Path);
public sealed record DevPathArgs(string Path);
/// <summary> Search result token; accepted as a string or a number. </summary>
public sealed record TokenArgs(JsonElement Token)
{
    public string Value => Token.ValueKind switch
    {
        JsonValueKind.String => Token.GetString() ?? string.Empty,
        JsonValueKind.Undefined => throw new ArgumentException("Missing 'token'."),
        _ => Token.GetRawText(),
    };
}
public sealed record IdArgs(string Id);
public sealed record EncounterSearchArgs(int Species, int Version, int[]? Moves, bool? Shiny, bool? Egg, string[]? Types);
