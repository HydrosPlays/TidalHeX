using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PKHeX.Core;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// tools.*, app.settings, app.classic: the SAV tab's sub-editor buttons (SAVEditor.ToggleViewSubEditors + click handlers).
internal sealed partial class WebApi
{
    private const string CatTrainer = "Trainer";
    private const string CatItems = "Pokédex & Items";
    private const string CatEvents = "Events";
    private const string CatWorld = "World";
    private const string CatBattle = "Battle";
    private const string CatData = "Data";

    /// <param name="Id">Stable id: the classic button name without its prefix.</param>
    /// <param name="Control">Classic control name, used for the translated display name (<c>Main.{Control}</c>).</param>
    private sealed record Tool(string Id, string Control, string Name, string Category, string Description, Func<SaveFile, bool> Visible, Action Open);

    private void RegisterTools()
    {
        Bridge.Register("tools.list", _ => GetToolList());
        Bridge.Register("tools.open", c => OpenTool(c.Get<IdArgs>().Id));
        Bridge.Register("app.settings", _ => OpenSettings());
        Bridge.Register("app.setOption", c => SetOption(c.Get<OptionArgs>()));
        Bridge.Register("app.setTheme", c => SetTheme(c.Get<ThemeArgs>().Theme));
        Bridge.Register("app.classic", _ => SwitchToClassic());
    }

    /// <summary> Development only (dev.showAllTools): offer the save editors for blank saves too. </summary>
    private static bool DevShowAllTools;

    private List<ToolInfo> GetToolList()
    {
        var lang = GameInfo.CurrentLanguage;
        return GetAvailableTools()
            .Select(z => new ToolInfo
            {
                Id = z.Id,
                Name = WinFormsTranslator.TranslateText($"Main.{z.Control}", z.Name, lang).TrimEnd('+'),
                Category = z.Category,
                Description = z.Description,
                Web = GetWebEditor(z.Id),
            })
            .OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase) // the classic panel sorts by text too
            .ToList();
    }

    /// <summary> Tools rebuilt in the page (the rest open PKHeX's classic windows). </summary>
    private string? GetWebEditor(string id) => id switch
    {
        "OpenItemPouch" => "items",
        "OpenTrainerInfo" => "trainer",
        "OpenBoxLayout" => "boxlayout",
        "OpenPokedex" when GetDexOps(SAV) is not null => "pokedex",
        _ => null,
    };

    private IEnumerable<Tool> GetAvailableTools()
    {
        var sav = SAV;
        if (sav is FakeSaveFile)
            return [];

        // Sub-editors are hidden for blank saves and bulk storage (ToggleViewSubEditors); the misc actions follow ToggleViewMisc.
        bool subEditors = (sav.State.Exportable || DevShowAllTools) && sav is not BulkStorage;
        var misc = GetMiscTools(subEditors);
        return subEditors ? GetSubEditors().Concat(misc).Where(z => z.Visible(sav)) : misc.Where(z => z.Visible(sav));
    }

    private bool OpenTool(string id)
    {
        var tool = GetAvailableTools().FirstOrDefault(z => z.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"The tool '{id}' is not available for this save file.");
        try
        {
            tool.Open();
        }
        finally
        {
            // Tools write straight into the save (boxes, names, wallpapers, party mail...): refresh everything.
            RefreshAllSlots();
        }
        return true;
    }

    private void ShowDialog(Form form)
    {
        using (form)
            form.ShowDialog(Host);
    }

    #region Sub-editors (FLP_SAVtools)

    private IEnumerable<Tool> GetSubEditors() =>
    [
        new("OpenTrainerInfo", "B_OpenTrainerInfo", "Trainer Info", CatTrainer, "Name, IDs, money, play time and other trainer details.",
            s => s.HasParty || s is SAV7b, () => ShowDialog(GetTrainerEditor(SAV))),
        new("OpenItemPouch", "B_OpenItemPouch", "Items", CatItems, "Edit the bag: items, key items, TMs, berries and other pouches.",
            s => (s.HasParty && s is not SAV4BR) || s is SAV7b, () => ShowDialog(new SAV_Inventory(SAV))),
        new("OpenBoxLayout", "B_OpenBoxLayout", "Box Layout", CatData, "Rename boxes, change wallpapers and box unlock state.",
            s => s is IBoxDetailName, () => ShowDialog(new SAV_BoxLayout(SAV, Session.CurrentBox))),
        new("OpenWondercards", "B_OpenWondercards", "Wonder Cards", CatEvents, "Manage the Mystery Gift cards stored in the save.",
            s => s is IMysteryGiftStorageProvider, () => ShowDialog(new SAV_Wondercard(SAV))),
        new("OpenOPowers", "B_OpenOPowers", "O-Powers", CatTrainer, "Unlock and level up O-Powers.",
            s => s is ISaveBlock6Main, () => ShowDialog(new SAV_OPower((ISaveBlock6Main)SAV))),
        new("OpenEventFlags", "B_OpenEventFlags", "Event Flags", CatEvents, "Toggle story event flags and edit event work values.",
            s => s is IEventFlag37 or IEventFlagProvider37 or SAV1 or SAV2 or SAV8BS or SAV7b or SAV9ZA, OpenEventFlags),
        new("OpenPokedex", "B_OpenPokedex", "Pokédex", CatItems, "Edit seen, caught and form data in the Pokédex.",
            s => s.HasPokeDex, OpenPokedex),
        new("OpenLinkInfo", "B_OpenLinkInfo", "Link Data", CatEvents, "Import or clear Pokémon Link gift data.",
            s => s is ISaveBlock6Main, () => ShowDialog(new SAV_Link6(SAV))),
        new("OpenBerryField", "B_OpenBerryField", "Berry Field", CatWorld, "View the berry field plots.",
            s => s is SAV6XY, () => ShowDialog(new SAV_BerryFieldXY((SAV6XY)SAV))),
        new("OpenPokeblocks", "B_OpenPokeblocks", "Pokéblocks", CatItems, "Edit the Pokéblock Kit.",
            s => s is SAV6AO, () => ShowDialog(new SAV_PokeBlockORAS((SAV6AO)SAV))),
        new("OpenSecretBase", "B_OpenSecretBase", "Secret Base", CatWorld, "Edit secret bases, their decorations and trainers.",
            s => s is SAV6AO or SAV3 { LargeBlock: ISaveBlock3LargeHoenn }, OpenSecretBase),
        new("OpenPokepuffs", "B_OpenPokepuffs", "Poké Puffs", CatItems, "Edit the Poké Puff case.",
            s => s is ISaveBlock6Main, () => ShowDialog(new SAV_Pokepuff((ISaveBlock6Main)SAV))),
        new("OpenSuperTraining", "B_OpenSuperTraining", "Super Training", CatBattle, "Edit Super Training records and training bags.",
            s => s is ISaveBlock6Main, () => ShowDialog(new SAV_SuperTrain((SAV6)SAV))),
        new("OpenHallofFame", "B_OpenHallofFame", "Hall of Fame", CatTrainer, "View and edit the Hall of Fame teams.",
            s => s is ISaveBlock6Main or SAV7 or SAV3 { IsMisconfiguredSize: false } or SAV1, OpenHallOfFame),
        new("OUTPasserby", "B_OUTPasserby", "Passerby", CatData, "Copy the PSS passerby list to the clipboard.",
            s => s is ISaveBlock6Main, CopyPasserby),
        new("DLC", "B_DLC", "DLC Editor", CatData, "Import or export downloadable content (musicals, C-Gear skins, Pokédex skins...).",
            s => s is SAV5 or SAV4HGSS or SAV4Pt, OpenDLC),
        new("Donuts", "B_Donuts", "Donuts", CatItems, "Edit your donuts.",
            s => s is SAV9ZA { SaveRevision: >= 1 }, () => ShowDialog(new SAV_Donut9a((SAV9ZA)SAV))),
        new("OpenPokeBeans", "B_OpenPokeBeans", "Poké Beans", CatItems, "Edit Poké Bean counts.",
            s => s is SAV7, () => ShowDialog(new SAV_Pokebean((SAV7)SAV))),
        new("CellsStickers", "B_CellsStickers", "Cells/Stickers", CatItems, "Edit collected Zygarde Cells and Totem Stickers.",
            s => s is SAV7, () => ShowDialog(new SAV_ZygardeCell((SAV7)SAV))),
        new("OpenMiscEditor", "B_OpenMiscEditor", "Misc Edits", CatTrainer, "Game-specific extras: records, facilities, coins and more.",
            s => s is SAV2 { Version: GameVersion.C } or SAV3 or SAV4 or SAV5 or SAV8BS, OpenMiscEditor),
        new("OpenHoneyTreeEditor", "B_OpenHoneyTreeEditor", "Honey Tree", CatWorld, "Edit honey tree encounters.",
            s => s is SAV4Sinnoh, () => ShowDialog(new SAV_HoneyTree((SAV4Sinnoh)SAV))),
        new("OpenFriendSafari", "B_OpenFriendSafari", "Friend Safari", CatWorld, "Unlock every Friend Safari slot.",
            s => s is SAV6XY, UnlockFriendSafari),
        new("OpenRTCEditor", "B_OpenRTCEditor", "Clock (RTC)", CatData, "Reset or adjust the in-game real-time clock.",
            s => (s.Generation == 2 && s is not SAV2Stadium) || s is SAV3 { SmallBlock: ISaveBlock3SmallHoenn }, OpenRTCEditor),
        new("OpenUGSEditor", "B_OpenUGSEditor", "Underground", CatWorld, "Edit the Underground: goods, spheres, traps and statues.",
            s => s is SAV4Sinnoh or SAV8BS, OpenUnderground),
        new("OpenGeonetEditor", "B_OpenGeonetEditor", "Geonet", CatEvents, "Edit the Geonet globe locations.",
            s => s is SAV4, () => ShowDialog(new SAV_Geonet4((SAV4)SAV))),
        new("OpenUnityTowerEditor", "B_OpenUnityTowerEditor", "Unity Tower", CatWorld, "Edit Unity Tower floors and visitors.",
            s => s is SAV5, () => ShowDialog(new SAV_UnityTower((SAV5)SAV))),
        new("OpenJoinAvenueEditor", "B_OpenJoinAvenueEditor", "Join Avenue", CatWorld, "Edit Join Avenue shops, rank and visitors.",
            s => s is SAV5B2W2, () => ShowDialog(new SAV_JoinAvenue((SAV5B2W2)SAV))),
        new("OpenPokeathlon", "B_OpenPokeathlon", "Pokéathlon", CatBattle, "Edit Pokéathlon records and points.",
            s => s is SAV4HGSS, () => ShowDialog(new SAV_Pokeathlon4((SAV4HGSS)SAV))),
        new("OpenMedalsEditor", "B_OpenMedalsEditor", "Medals", CatTrainer, "Edit Medal Rally medals.",
            s => s is SAV5B2W2, () => ShowDialog(new SAV_Medals5((SAV5B2W2)SAV))),
        new("OpenChatterEditor", "B_OpenChatterEditor", "Chatter", CatTrainer, "Edit Chatot's recorded Chatter.",
            s => s is SAV4 or SAV5, () => ShowDialog(new SAV_Chatter(SAV))),
        new("Roamer", "B_Roamer", "Roamer", CatWorld, "Edit roaming legendary Pokémon.",
            s => s is SAV3 or SAV6XY, OpenRoamer),
        new("FestivalPlaza", "B_FestivalPlaza", "Festival Plaza", CatWorld, "Edit Festival Plaza facilities, rank and coins.",
            s => s is SAV7, OpenFestivalPlaza),
        new("MailBox", "B_MailBox", "Mailbox", CatItems, "Edit mail held by party Pokémon and stored in the PC mailbox.",
            s => s is SAV2 or SAV2Stadium or SAV3 or SAV4 or SAV5, OpenMailBox),
        new("OpenApricorn", "B_OpenApricorn", "Apricorns", CatItems, "Edit the Apricorn Box.",
            s => s is SAV4HGSS, () => ShowDialog(new SAV_Apricorn((SAV4HGSS)SAV))),
        new("OpenGiftRibbons", "B_OpenGiftRibbons", "Gift Ribbons", CatEvents, "Edit the event ribbon descriptions stored in the save.",
            s => s is IGiftRibbons, () => ShowDialog(new SAV_GiftRibbons((IGiftRibbons)SAV))),
        new("Raids", "B_Raids", "Raids", CatBattle, "Edit raid dens / Tera Raid crystals.",
            s => s is SAV8SWSH or SAV9SV, () => OpenRaids(0)),
        new("RaidsDLC1", "B_RaidsDLC1", "Raids (DLC 1)", CatBattle, "Edit Isle of Armor dens / Kitakami Tera Raids.",
            s => s is SAV8SWSH { SaveRevision: >= 1 } or SAV9SV { SaveRevision: >= 1 }, () => OpenRaids(1)),
        new("RaidsDLC2", "B_RaidsDLC2", "Raids (DLC 2)", CatBattle, "Edit Crown Tundra dens / Blueberry Academy Tera Raids.",
            s => s is SAV8SWSH { SaveRevision: >= 2 } or SAV9SV { SaveRevision: >= 2 }, () => OpenRaids(2)),
        new("Blocks", "B_Blocks", "Block Data", CatData, "Low-level save block editor for advanced users.",
            _ => true, OpenBlocks),
        new("OtherSlots", "B_OtherSlots", "Other Slots", CatBattle, "View the registered Stadium teams.",
            s => s is SAV1StadiumJ or SAV1Stadium or SAV2Stadium, OpenOtherSlots),
        new("OpenSealStickers", "B_OpenSealStickers", "Seal Stickers", CatItems, "Edit ball capsule seal stickers.",
            s => s is SAV8BS, () => ShowDialog(new SAV_SealStickers8b((SAV8BS)SAV))),
        new("Poffins", "B_Poffins", "Poffins", CatItems, "Edit the Poffin Case.",
            s => s is SAV8BS, () => ShowDialog(new SAV_Poffin8b((SAV8BS)SAV))),
        new("RaidsSevenStar", "B_RaidsSevenStar", "Raids (7 Star)", CatBattle, "Edit 7-star Tera Raid event progress.",
            s => s is SAV9SV, () => OpenRaids(3)),
        new("OpenBattlePass", "B_OpenBattlePass", "Battle Passes", CatBattle, "Edit Battle Revolution battle passes.",
            s => s is SAV4BR, OpenBattlePass),
        new("OpenGear", "B_OpenGear", "Gear", CatTrainer, "Unlock Battle Revolution trainer gear.",
            s => s is SAV4BR, () => ShowDialog(new SAV_Gear((SAV4BR)SAV))),
        new("OpenFashion", "B_OpenFashion", "Fashion", CatTrainer, "Unlock clothing and accessories.",
            s => s is SAV9SV or SAV9ZA, OpenFashion),
        new("OpenGlobalLink", "B_OpenGlobalLink", "Pokémon Global Link", CatEvents, "Edit Pokémon Global Link (Dream World) data.",
            s => s is SAV5, () => ShowDialog(new SAV_GlobalLink5((SAV5)SAV))),
    ];

    private static Form GetTrainerEditor(SaveFile sav) => sav switch
    {
        SAV6 s6 => new SAV_Trainer(s6),
        SAV7 s7 => new SAV_Trainer7(s7),
        SAV7b b7 => new SAV_Trainer7GG(b7),
        SAV8SWSH swsh => new SAV_Trainer8(swsh),
        SAV8BS bs => new SAV_Trainer8b(bs),
        SAV8LA la => new SAV_Trainer8a(la),
        SAV9SV sv => new SAV_Trainer9(sv),
        SAV9ZA za => new SAV_Trainer9a(za),
        SAV4BR br => new SAV_Trainer4BR(br),
        _ => new SAV_SimpleTrainer(sav),
    };

    private void OpenDLC()
    {
        if (SAV is SAV5 s5)
            ShowDialog(new SAV_DLC5(s5));
        else if (SAV is SAV4 s4)
            ShowDialog(new SAV_DLC4(s4));
    }

    private void OpenSecretBase()
    {
        if (SAV is SAV3 s3)
            ShowDialog(new SAV_SecretBase3(s3));
        else if (SAV is SAV6AO ao)
            ShowDialog(new SAV_SecretBase(ao));
    }

    private void OpenRoamer()
    {
        if (SAV is SAV3 s3)
            ShowDialog(new SAV_Roamer3(s3));
        else if (SAV is SAV6XY xy)
            ShowDialog(new SAV_Roamer6(xy));
    }

    private void OpenEventFlags()
    {
        var sav = SAV;
        Form form = sav switch
        {
            SAV1 s => new SAV_EventReset1(s),
            SAV7b s => new SAV_EventWork(s),
            SAV8BS s => new SAV_FlagWork8b(s),
            IEventFlag37 g37 => new SAV_EventFlags(g37, sav.Version),
            IEventFlagProvider37 p => new SAV_EventFlags(p.EventWork, sav.Version),
            SAV2 s => new SAV_EventFlags2(s),
            SAV9ZA za => new SAV_FlagWork9a(za),
            _ => throw new NotSupportedException("Event flags are not supported for this save file."),
        };
        ShowDialog(form);
    }

    /// <param name="index">0: base game, 1: DLC 1, 2: DLC 2, 3: seven-star (SV only).</param>
    private void OpenRaids(int index)
    {
        if (SAV is SAV9SV sv)
        {
            Form? form = index switch
            {
                0 => new SAV_Raid9(sv, TeraRaidOrigin.Paldea),
                1 => new SAV_Raid9(sv, TeraRaidOrigin.Kitakami),
                2 => new SAV_Raid9(sv, TeraRaidOrigin.BlueberryAcademy),
                3 => new SAV_RaidSevenStar9(sv),
                _ => null,
            };
            if (form is not null)
                ShowDialog(form);
        }
        else if (SAV is SAV8SWSH swsh)
        {
            Form? form = index switch
            {
                0 => new SAV_Raid8(swsh, MaxRaidOrigin.Galar),
                1 => new SAV_Raid8(swsh, MaxRaidOrigin.IsleOfArmor),
                2 => new SAV_Raid8(swsh, MaxRaidOrigin.CrownTundra),
                _ => null,
            };
            if (form is not null)
                ShowDialog(form);
        }
    }

    /// <summary> Classic shows this non-modally next to the PKM editor; here it is modal and loads into the web editor. </summary>
    private void OpenOtherSlots()
    {
        if (SAV is SAV_STADIUM s0)
            ShowDialog(new SAV_GroupViewer(s0, new WebPKMView(this), s0.GetRegisteredTeams()));
    }

    /// <summary> Classic shows this non-modally next to the PKM editor; here it is modal and uses the web editor. </summary>
    private void OpenBattlePass()
    {
        if (SAV is SAV4BR br)
            ShowDialog(new SAV_BattlePass(br, new WebPKMView(this)));
    }

    private void OpenBlocks() => ShowDialog(GetAccessorForm(SAV));

    private static Form GetAccessorForm(SaveFile sav) => sav switch
    {
        SAV5BW s => new SAV_Accessor<SaveBlockAccessor5BW>(s, s.Blocks),
        SAV5B2W2 s => new SAV_Accessor<SaveBlockAccessor5B2W2>(s, s.Blocks),
        SAV6XY s => new SAV_Accessor<SaveBlockAccessor6XY>(s, s.Blocks),
        SAV6AO s => new SAV_Accessor<SaveBlockAccessor6AO>(s, s.Blocks),
        SAV6AODemo s => new SAV_Accessor<SaveBlockAccessor6AODemo>(s, s.Blocks),
        SAV7SM s => new SAV_Accessor<SaveBlockAccessor7SM>(s, s.Blocks),
        SAV7USUM s => new SAV_Accessor<SaveBlockAccessor7USUM>(s, s.Blocks),
        SAV7b s => new SAV_Accessor<SaveBlockAccessor7b>(s, s.Blocks),
        ISCBlockArray s => new SAV_BlockDump8(s),
        _ => GetPropertyForm(sav),
    };

    private static Form GetPropertyForm(object sav)
    {
        var form = new Form
        {
            Text = WinFormsTranslator.TranslateText(Controls.SAVEditor.SimpleEditorKey, "Simple Editor", GameInfo.CurrentLanguage),
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new System.Drawing.Size(350, 380),
            MinimizeBox = false,
            MaximizeBox = false,
            Icon = Properties.Resources.Icon,
        };
        var pg = new PropertyGrid { Dock = DockStyle.Fill };
        PropertyGridLocalization.Apply(pg, sav, GameInfo.CurrentLanguage);
        form.Controls.Add(pg);
        return form;
    }

    private void UnlockFriendSafari()
    {
        if (SAV is not SAV6XY xy)
            return;

        var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgSaveGen6FriendSafari, MsgSaveGen6FriendSafariCheatDesc);
        if (dr == DialogResult.Yes)
            xy.UnlockAllFriendSafariSlots();
    }

    private void OpenPokedex()
    {
        Form? form = SAV switch
        {
            SAV1 s1 => new SAV_SimplePokedex(s1),
            SAV2 s2 => new SAV_SimplePokedex(s2),
            SAV3 s3 => new SAV_SimplePokedex(s3),
            SAV4 s4 => new SAV_Pokedex4(s4),
            SAV5 s5 => new SAV_Pokedex5(s5),
            SAV6XY xy => new SAV_PokedexXY(xy),
            SAV6AO ao => new SAV_PokedexORAS(ao),
            SAV7 s7 => new SAV_PokedexSM(s7),
            SAV7b b7 => new SAV_PokedexGG(b7),
            SAV8SWSH swsh => new SAV_PokedexSWSH(swsh),
            SAV8BS bs => new SAV_PokedexBDSP(bs),
            SAV8LA la => new SAV_PokedexLA(la),
            SAV9SV sv => sv.SaveRevision == 0 ? new SAV_PokedexSV(sv) : new SAV_PokedexSVKitakami(sv),
            SAV9ZA za => new SAV_Pokedex9a(za),
            _ => null,
        };
        if (form is not null)
            ShowDialog(form);
    }

    private void OpenMiscEditor()
    {
        Form? form = SAV switch
        {
            SAV2 sav2 => new SAV_Misc2(sav2),
            SAV3 sav3 => new SAV_Misc3(sav3),
            SAV4 sav4 => new SAV_Misc4(sav4),
            SAV5 sav5 => new SAV_Misc5(sav5),
            SAV8BS bs => new SAV_Misc8b(bs),
            _ => null,
        };
        if (form is not null)
            ShowDialog(form);
    }

    private void OpenRTCEditor()
    {
        switch (SAV.Generation)
        {
            case 2:
                var sav2 = (SAV2)SAV;
                var msg = MsgSaveGen2RTCResetBitflag;
                if (!sav2.Japanese) // show Reset Key for non-Japanese saves
                    msg = string.Format(MsgSaveGen2RTCResetPassword, sav2.ResetKey) + Environment.NewLine + Environment.NewLine + msg;
                var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, msg);
                if (dr == DialogResult.Yes)
                    sav2.ResetRTC();
                break;
            case 3:
                ShowDialog(new SAV_RTC3(SAV));
                break;
        }
    }

    private void CopyPasserby()
    {
        if (SAV.Generation != 6)
            return;
        if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgSaveGen6Passerby))
            return;
        var result = PSS6.GetPSSParse((SAV6)SAV);
        WinFormsUtil.SetClipboardText(string.Join(Environment.NewLine, result));
    }

    private void OpenHallOfFame()
    {
        Form? form = SAV switch
        {
            SAV1 s1 => new SAV_HallOfFame1(s1),
            SAV3 s3 => new SAV_HallOfFame3(s3),
            SAV6 s6 => new SAV_HallOfFame(s6),
            SAV7 s7 => new SAV_HallOfFame7(s7),
            _ => null,
        };
        if (form is not null)
            ShowDialog(form);
    }

    private void OpenFashion()
    {
        Form? form = SAV switch
        {
            SAV9SV s9sv => new SAV_Fashion9(s9sv),
            SAV9ZA s9za => new SAV_Fashion9(s9za),
            _ => null,
        };
        if (form is not null)
            ShowDialog(form);
    }

    private void OpenUnderground()
    {
        if (SAV is SAV4Sinnoh s)
            ShowDialog(new SAV_Underground(s));
        else if (SAV is SAV8BS bs)
            ShowDialog(new SAV_Underground8b(bs));
    }

    private void OpenFestivalPlaza()
    {
        if (SAV is SAV7 s)
            ShowDialog(new SAV_FestivalPlaza(s));
    }

    private void OpenMailBox() => ShowDialog(new SAV_MailBox(SAV)); // party mail changes are picked up by the refresh

    #endregion

    #region Misc actions (FLP_SAVToolsMisc)

    private IEnumerable<Tool> GetMiscTools(bool subEditors) =>
    [
        new("SaveBoxBin", "B_SaveBoxBin", "Save Box Data", CatData, "Export all boxes or the current box as raw box data (.bin).",
            s => s.HasBox, SaveBoxBinary),
        new("VerifyCHK", "B_VerifyCHK", "Verify Checksums", CatData, "Check whether the save's checksums are valid.",
            s => s.State.Exportable, VerifyChecksums),
        new("VerifySaveEntities", "B_VerifySaveEntities", "Verify All PKMs", CatData, "Legality check every Pokémon stored in the save.",
            _ => true, VerifyStoredEntities),
        new("ExportBAK", "Menu_ExportBAK", "Export Backup", CatData, "Copy the original save file, as it was loaded, to a new location.",
            s => s.State.Exportable && s.Metadata.FilePath is not null, () => ExportBackup()),
        new("JPEG", "B_JPEG", "Save PGL .JPEG", CatData, "Export the Pokémon Global Link photo stored in the save.",
            s => subEditors && s is ISaveBlock6Main, ExportJpeg),
        new("MyWallpaper", "B_MyWallpaper", "Save My Wallpaper", CatData, "Export the My Wallpaper picture stored in the save as a PNG.",
            s => subEditors && s is SAV3RSBox, ExportMyWallpaper),
        new("ConvertKorean", "B_ConvertKorean", "Korean Save Conversion", CatData, "Convert a Gen 4 save between Korean and international.",
            s => subEditors && s is SAV4, ConvertKorean),
    ];

    /// <summary> SAVEditor.B_SaveBoxBin_Click → BoxEditor.SaveBoxBinary </summary>
    private void SaveBoxBinary()
    {
        var sav = SAV;
        if (!sav.HasBox)
        {
            WinFormsUtil.Alert(MsgSaveBoxFailNone);
            return;
        }

        var box = Session.CurrentBox;
        var boxName = GetBoxName(box);
        var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel,
            MsgSaveBoxExportYes + Environment.NewLine +
            string.Format(MsgSaveBoxExportNo, boxName, box + 1) + Environment.NewLine +
            MsgSaveBoxExportCancel);

        if (dr == DialogResult.Yes)
        {
            using var sfd = new SaveFileDialog();
            sfd.Filter = "Box Data|*.bin";
            sfd.FileName = "pcdata.bin";
            if (sfd.ShowDialog(Host) != DialogResult.OK)
                return;
            File.WriteAllBytes(sfd.FileName, sav.GetPCBinary());
        }
        else if (dr == DialogResult.No)
        {
            using var sfd = new SaveFileDialog();
            sfd.Filter = "Box Data|*.bin";
            sfd.FileName = $"boxdata {boxName}.bin";
            if (sfd.ShowDialog(Host) != DialogResult.OK)
                return;
            File.WriteAllBytes(sfd.FileName, sav.GetBoxBinary(box));
        }
    }

    private void VerifyChecksums()
    {
        var sav = SAV;
        if (sav.State.Edited)
        {
            WinFormsUtil.Alert(MsgSaveChecksumFailEdited);
            return;
        }
        if (sav.ChecksumsValid)
        {
            WinFormsUtil.Alert(MsgSaveChecksumValid);
            return;
        }

        if (DialogResult.Yes == WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgSaveChecksumFailExport))
            WinFormsUtil.SetClipboardText(sav.ChecksumInfo);
    }

    private void VerifyStoredEntities()
    {
        var bulk = new Core.Bulk.BulkAnalysis(SAV, Settings.Legality.Bulk);
        if (bulk.Valid)
        {
            WinFormsUtil.Alert("Clean!");
            return;
        }

        if (WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgClipboardLegalityExport) != DialogResult.Yes)
            return;

        var localization = LegalityLocalizationSet.GetLocalization(GameInfo.CurrentLanguage);
        var msg = bulk.Report(localization);
        WinFormsUtil.SetClipboardText(msg);
        WinFormsUtil.Asterisk();
    }

    /// <summary> SAVEditor.ExportBackup </summary>
    private bool ExportBackup()
    {
        var sav = SAV;
        if (!sav.State.Exportable || sav.Metadata.FilePath is not { } file)
            return false;

        if (!File.Exists(file))
        {
            WinFormsUtil.Error(MsgSaveBackupNotFound, file);
            return false;
        }

        var suggestion = PathUtil.CleanFileName(sav.Metadata.BAKName);
        using var sfd = new SaveFileDialog();
        sfd.FileName = suggestion;
        if (sfd.ShowDialog(Host) != DialogResult.OK)
            return false;

        string path = sfd.FileName;
        if (!File.Exists(file)) // did they move it again?
        {
            WinFormsUtil.Error(MsgSaveBackupNotFound, file);
            return false;
        }
        File.Copy(file, path, true);
        WinFormsUtil.Alert(MsgSaveBackup, path);

        return true;
    }

    private void ExportJpeg()
    {
        var s6 = (SAV6)SAV;
        var jpeg = s6.GetJPEGData();
        if (jpeg.Length == 0)
        {
            WinFormsUtil.Alert(MsgSaveJPEGExportFail);
            return;
        }
        string filename = $"{s6.JPEGTitle}'s picture";
        using var sfd = new SaveFileDialog();
        sfd.FileName = filename;
        sfd.Filter = "JPEG|*.jpeg";
        if (sfd.ShowDialog(Host) != DialogResult.OK)
            return;
        File.WriteAllBytes(sfd.FileName, jpeg);
    }

    /// <summary> SAVEditor.B_MyWallpaper_Click </summary>
    private void ExportMyWallpaper()
    {
        var box = (SAV3RSBox)SAV;
        var cmpr = box.MyWallpaper;
        if (!(box.MyWallpaperEnabled || cmpr.ContainsAnyExcept<byte>(0)))
        {
            WinFormsUtil.Alert(MsgSaveJPEGExportFail);
            return;
        }
        const int width = SAV3RSBox.WP_WIDTH, height = SAV3RSBox.WP_HEIGHT;
        var data = CMPR.Decompress(cmpr, width, height);
        using var picture = Drawing.ImageUtil.GetBitmap(data, width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var sfd = new SaveFileDialog();
        sfd.FileName = "My Wallpaper";
        sfd.Filter = "PNG|*.png";
        if (sfd.ShowDialog(Host) != DialogResult.OK)
            return;
        picture.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void ConvertKorean()
    {
        if (SAV.Generation != 4)
            return;
        var s4 = (SAV4)SAV;
        var isKorean = s4.Magic == SAV4.MAGIC_KOREAN;
        var msg = isKorean ? MsgSaveGen4ConvertInternational : MsgSaveGen4ConvertKorean;
        if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, msg))
            return;
        s4.Magic = isKorean ? SAV4.MAGIC_JAPAN_INTL : SAV4.MAGIC_KOREAN;
        SAV.State.Edited = true;
    }

    #endregion

    #region App

    /// <summary> Settings the pages change in place (same values as the classic Settings window; saved on exit). </summary>
    private UiSettings SetOption(OptionArgs args)
    {
        switch (args.Name)
        {
            case "encountersInGameOnly":
                Settings.EncounterDb.FilterUnavailableSpecies = args.Value;
                break;
            case "giftsInGameOnly":
                Settings.MysteryDb.FilterUnavailableSpecies = args.Value;
                Session.ResetGifts();
                break;
            case "reducedMotion":
                Settings.Startup.TidalReduceMotion = args.Value;
                break;
            case "checkForUpdates":
                Settings.Startup.TidalCheckForUpdates = args.Value;
                break;
            default:
                throw new ArgumentException($"Unknown option '{args.Name}'.");
        }
        return GetUiSettings();
    }

    /// <summary> Settings → Theme: "light" (Tidal Light), "dark" (Tidal Dark), "pss" (Tidal PSS), "za" (Tidal ZA) or "pixel" (Tidal Pixel), saved on exit. </summary>
    private UiSettings SetTheme(string theme)
    {
        Settings.Startup.TidalUITheme = theme switch
        {
            "light" => TidalUITheme.Light,
            "dark" => TidalUITheme.Dark,
            "pss" => TidalUITheme.PSS,
            "za" => TidalUITheme.ZA,
            "pixel" => TidalUITheme.Pixel,
            _ => throw new ArgumentException($"Unknown theme '{theme}'."),
        };
        return GetUiSettings();
    }

    /// <summary>
    /// Main.MainMenuSettings: the classic settings dialog, then reapply everything.
    /// </summary>
    private UiSettings OpenSettings()
    {
        using (var form = new SettingsEditor(Settings))
        {
            form.ShowDialog(Host);
            ReloadProgramSettings();
            Session.ResetGifts(); // the gift list depends on the Mystery Gift database settings

            if (form.BlankChanged) // changed by user
            {
                LoadBlankSaveFile(Settings.Startup.DefaultSaveVersion);
                return GetUiSettings();
            }
        }

        // Sprite and legality display settings may have changed.
        Session.TouchEditor();
        RefreshAllSlots();
        return GetUiSettings();
    }

    /// <summary>
    /// Restarts in the classic PKHeX window (after the usual unsaved-changes prompt).
    /// </summary>
    private bool SwitchToClassic()
    {
        if (!Host.ConfirmDiscardChanges())
            return false;
        Settings.Startup.TidalUI = false;
        Host.RestartInClassicMode();
        return true;
    }

    #endregion
}
