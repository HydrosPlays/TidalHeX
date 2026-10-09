using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using PKHeX.Core;

namespace PKHeX.WinForms;

public sealed class StartupSettings : IStartupSettings
{
    [Browsable(false)]
    [LocalizedDescription("Last version that the program was run with.")]
    public string Version { get; set; } = string.Empty;

    [LocalizedDescription("Use the Dark color mode for the application on startup.")]
    public bool DarkMode { get; set; } = Application.SystemColorMode == SystemColorMode.Dark; // auto-detect for new settings, json load preserves any choice.

    [LocalizedDescription("Use the TidalHeX ocean theme for all windows (restart required). Overrides DarkMode when enabled.")]
    public bool TidalTheme { get; set; } = true;

    [LocalizedDescription("Use the TidalHeX web interface instead of the classic PKHeX window (restart required). Requires the Tidal theme.")]
    public bool TidalUI { get; set; } = true;

    [LocalizedDescription("Check GitHub for a new TidalHeX release at startup.")]
    public bool TidalCheckForUpdates { get; set; } = true;

    [Browsable(false)]
    [LocalizedDescription("A TidalHeX release the user chose to skip (no reminder until a newer one comes out).")]
    public string TidalSkippedUpdate { get; set; } = string.Empty;

    [LocalizedDescription("Color theme of the TidalHeX web interface: Tidal Light (the default), Tidal Dark, Tidal PSS, Tidal ZA, Tidal Pixel or Tidal Arceus.")]
    public TidalUITheme TidalUITheme { get; set; } = TidalUITheme.Light;

    [LocalizedDescription("Turn off the bubbles and animations in the TidalHeX web interface.")]
    public bool TidalReduceMotion { get; set; } = Tidal.Web.WebApi.IsSystemReducedMotion(); // follows Windows for new settings, json load preserves any choice.

    [LocalizedDescription("Force HaX mode on Program Launch")]
    public bool ForceHaXOnLaunch { get; set; }

    [LocalizedDescription("Toggles a higher Dpi rendering mode for the application on startup.")]
    public bool HighDpiText { get; set; } // opt-in

    [LocalizedDescription("Skips displaying the splash screen on Program Launch.")]
    public bool SkipSplashScreen { get; set; }

    [LocalizedDescription("Automatically locates the most recently saved Save File when opening a new file.")]
    public bool TryDetectRecentSave { get; set; } = true;

    [LocalizedDescription("Automatically Detect Save File on Program Startup")]
    public SaveFileLoadSetting AutoLoadSaveOnStartup { get; set; } = SaveFileLoadSetting.RecentBackup;

    [LocalizedDescription("Show the changelog when a new version of the program is run for the first time.")]
    public bool ShowChangelogOnUpdate { get; set; } = true;

    [LocalizedDescription("Loads plugins from the plugins folder, assuming the folder exists.")]
    public bool PluginLoadEnable { get; set; } = true;

    [LocalizedDescription("Loads any plugins that were merged into the main executable file.")]
    public bool PluginLoadMerged { get; set; }

    [Browsable(false)]
    public List<string> RecentlyLoaded { get; set; } = new(DefaultMaxRecent);

    private const int DefaultMaxRecent = 10;

    [LocalizedDescription("Amount of recently loaded save files to remember.")]
    public uint RecentlyLoadedMaxCount
    {
        get;
        // Sanity check to not let the user foot-gun themselves a slow recall time.
        set => field = Math.Clamp(value, 1, 1000);
    } = DefaultMaxRecent;

    // Don't let invalid values slip into the startup version.

    [Browsable(false)]
    public string Language
    {
        get;
        set
        {
            if (!GameLanguage.IsLanguageValid(value))
            {
                // Migrate old language codes set in earlier versions.
                field = value switch
                {
                    "zh" => "zh-Hans",
                    "zh2" => "zh-Hant",
                    _ => field,
                };
                return;
            }

            field = value;
        }
    } = WinFormsUtil.GetCultureLanguage();

    [Browsable(false)]
    public GameVersion DefaultSaveVersion
    {
        get;
        set
        {
            if (!value.IsValidSavedVersion())
                return;
            field = value;
        }
    } = Latest.Version;

    public void LoadSaveFile(string path)
    {
        var recent = RecentlyLoaded;
        // Remove from list if already present.
        if (!recent.Remove(path) && recent.Count >= RecentlyLoadedMaxCount)
            recent.RemoveAt(recent.Count - 1);
        recent.Insert(0, path);
    }
}

/// <summary> Color themes of the TidalHeX web interface. </summary>
public enum TidalUITheme
{
    /// <summary> Tidal Light: the bright cyan-to-blue sea (default). </summary>
    Light,

    /// <summary> Tidal Dark: the same design on a deep night-time sea. </summary>
    Dark,

    /// <summary> Tidal PSS: the colors of X/Y and Omega Ruby/Alpha Sapphire's Player Search System screen. </summary>
    PSS,

    /// <summary> Tidal ZA: Pokémon Legends: Z-A's dark, particle-filled title screen and slate menus. </summary>
    ZA,

    /// <summary> Tidal Pixel: the Game Boy / Game Boy Advance games' pixel menus, with its own pixel font. </summary>
    Pixel,

    /// <summary> Tidal Arceus: Pokémon Legends: Arceus's menus (ink strokes, round slots, a cream tab strip, parchment lists). </summary>
    Arceus, // saved as a number: new themes go at the end
}
