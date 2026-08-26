using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Theme;
using Common;

namespace UnitTests
{
    /// <summary>
    /// Covers translating an AO2 theme's courtroom_design.ini into an Oceanya panel layout.
    /// </summary>
    [TestFixture]
    public class Ao2ThemeLayoutImporterTests
    {
        [Test]
        public void Translate_MapsTheCoreWidgetsAndReservesTheClientsStrip()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700",
                ["viewport"] = "477, 49, 512, 384",
                ["ic_chatlog"] = "237, 50, 227, 350",
                ["server_chatlog"] = "1001, 50, 211, 211",
                ["ao2_ic_chat_message"] = "542, 433, 447, 23",
                ["emotes"] = "49, 531, 400, 120",
                ["hold_it"] = "762, 461, 73, 25"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            double shift = Ao2ThemeLayoutImporter.ClientsStripWidth;
            Assert.That(result.Layout.Panels["viewport"].Left, Is.EqualTo(477 + shift));
            Assert.That(result.Layout.Panels["viewport"].Top, Is.EqualTo(49));
            Assert.That(result.Layout.Panels["viewport"].Width, Is.EqualTo(512));
            Assert.That(result.Layout.Panels["ic_log"].Left, Is.EqualTo(237 + shift));
            Assert.That(result.Layout.Panels["ooc_log"].Width, Is.EqualTo(211));
            Assert.That(result.Layout.Panels["ic_message"].Left, Is.EqualTo(542 + shift));
            Assert.That(result.Layout.Panels["ic_emote_grid"].Height, Is.EqualTo(120));
            Assert.That(result.Layout.Panels["shout_holdit"].Left, Is.EqualTo(762 + shift));

            Assert.That(result.SurfaceWidth, Is.EqualTo(1262 + shift), "The surface widens for the clients strip.");
            // The Oceanya connection bar sits above the canvas the theme describes, so the window has to
            // be that much taller or the theme's bottom row of widgets falls outside it.
            Assert.That(result.SurfaceHeight, Is.EqualTo(700 + Ao2ThemeLayoutImporter.TopBarHeight));

            // The size has to be part of the layout itself, otherwise the import is clipped to the
            // stock surface and the user cannot see what was imported.
            Assert.That(result.Layout.SurfaceWidth, Is.EqualTo(1262 + shift));
            Assert.That(result.Layout.SurfaceHeight, Is.EqualTo(700 + Ao2ThemeLayoutImporter.TopBarHeight));

            // AO2 fits fixed-size emote buttons into the emote area rather than stretching them, so the
            // theme's button metrics arrive as the grid's item size.
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemSize, Is.EqualTo(40));
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemHeight, Is.EqualTo(40));
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemSpacingX, Is.EqualTo(9));

            // AO2's emote area is nothing but buttons; its arrows are widgets of their own.
            Assert.That(result.Layout.Panels["ic_emote_grid"].ReservePagingSpace, Is.False);
        }

        [Test]
        public void Translate_HidesPanelsTheThemeDoesNotDescribe()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 714, 668",
                ["ic_chatlog"] = "260, 0, 231, 319"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            // AO2 hides widgets its theme does not mention, so unmapped panels are hidden, not left
            // floating at their Oceanya defaults.
            Assert.That(result.Layout.Panels["shout_holdit"].IsHidden, Is.True);
            Assert.That(result.HiddenPanelIds, Does.Contain("shout_holdit"));

            // Cosmetic Oceanya-only panels are always hidden so the theme's own look comes through.
            Assert.That(result.Layout.Panels["ic_loremaster"].IsHidden, Is.True);
            Assert.That(result.Layout.Panels["ic_catchphrase"].IsHidden, Is.True);
        }

        [Test]
        public void Translate_KeepsTheClientsStripVisibleOnTheLeft()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 714, 668",
                ["ic_chatlog"] = "260, 0, 231, 319"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            OceanyaPanelPlacementState clients = result.Layout.Panels["clients_list"];
            Assert.That(clients.IsHidden, Is.False, "GM multi-client has no AO2 equivalent and must survive an import.");
            Assert.That(clients.Left, Is.LessThan(Ao2ThemeLayoutImporter.ClientsStripWidth));
            Assert.That(clients.Height, Is.GreaterThan(100), "The strip should use the height the theme leaves free.");
            Assert.That(result.Layout.Panels["clients_title"].IsHidden, Is.False);
            Assert.That(result.Layout.Panels["clients_add"].Left, Is.LessThan(Ao2ThemeLayoutImporter.ClientsStripWidth));
        }

        [Test]
        public void Translate_AppliesTheThemeScalingFactor()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 400, 300",
                ["ic_chatlog"] = "10, 20, 100, 50"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design, scalingFactor: 2);

            Assert.That(result.Layout.Panels["ic_log"].Left, Is.EqualTo(20 + Ao2ThemeLayoutImporter.ClientsStripWidth));
            Assert.That(result.Layout.Panels["ic_log"].Top, Is.EqualTo(40));
            Assert.That(result.Layout.Panels["ic_log"].Width, Is.EqualTo(200));
            Assert.That(result.SurfaceHeight, Is.EqualTo(600 + Ao2ThemeLayoutImporter.TopBarHeight));
        }

        [Test]
        public void Translate_TakesTheEmoteButtonMetricsFromTheTheme()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 400, 300",
                ["emotes"] = "0, 0, 400, 120",
                ["emote_button_size"] = "24, 30",
                ["emote_button_spacing"] = "0, 0"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            // Not square: AO2 button metrics are a `w, h` pair.
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemSize, Is.EqualTo(24));
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemHeight, Is.EqualTo(30));
            Assert.That(result.Layout.Panels["ic_emote_grid"].ItemSpacingX, Is.EqualTo(0));
        }

        [Test]
        public void Translate_IgnoresNonRectangleAndZeroSizedEntries()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 714, 668",
                ["ooc_default_color"] = "0, 0, 0",
                ["sfx_label"] = "0, 0, 0, 0",
                ["ic_chatlog"] = "260, 0, 231, 319"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            Assert.That(result.Layout.Panels.ContainsKey("ic_log"), Is.True);
            Assert.That(result.UnmappedIdentifiers, Does.Not.Contain("ooc_default_color"), "Colour triplets are not rectangles.");
        }

        [Test]
        public void Translate_ReportsIdentifiersOceanyaHasNoPanelFor()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 714, 668",
                ["evidence_buttons"] = "73, 55, 410, 276",
                ["defense_bar"] = "283, 412, 141, 15",
                ["evidence_button"] = "394, 20, 71, 25"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            // The evidence LIST has no counterpart; the evidence button and the health bar both do now,
            // even though the button's whole job is to report that we do not support evidence.
            Assert.That(result.UnmappedIdentifiers, Does.Contain("evidence_buttons"));
            Assert.That(result.UnmappedIdentifiers, Does.Not.Contain("evidence_button"));
            Assert.That(result.UnmappedIdentifiers, Does.Not.Contain("defense_bar"));
            Assert.That(result.Layout.Panels["bar_button_evidence"].IsHidden, Is.False);
            Assert.That(result.Layout.Panels["judge_defence_bar"].Width, Is.EqualTo(141));
        }

        [Test]
        public void ParseDesignFile_ReadsCommentsAliasesAndInconsistentCasing()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ini");
            File.WriteAllText(path, string.Join("\n", new[]
            {
                "/*====Window Size====*/",
                "courtroom = 0, 0, 1262, 700",
                "; a comment",
                "Additive = 564, 466, 64, 15",
                "hold_it = 762, 461, 73, 25"
            }));

            try
            {
                Dictionary<string, string> entries = Ao2ThemeLayoutImporter.ParseDesignFile(path);

                Assert.That(entries["courtroom"], Is.EqualTo("0, 0, 1262, 700"));
                Assert.That(entries["additive"], Is.EqualTo("564, 466, 64, 15"), "Keys must resolve case-insensitively.");

                Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(entries);
                Assert.That(result.Layout.Panels["ic_check_additive"].IsHidden, Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ResolveDesignFilePath_LooksInsideTheThemesFolderOfEachBaseFolder()
        {
            string baseFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string themeFolder = Path.Combine(baseFolder, "themes", "ProbeTheme");
            Directory.CreateDirectory(themeFolder);
            string designPath = Path.Combine(themeFolder, "courtroom_design.ini");
            File.WriteAllText(designPath, "courtroom = 0, 0, 714, 668");

            List<string> originalBaseFolders = Globals.BaseFolders?.ToList() ?? new List<string>();
            try
            {
                Globals.BaseFolders = new List<string> { baseFolder };

                // Theme roots are <base>/themes/<name>; resolving <base>/<name> found nothing at all.
                Assert.That(Ao2ThemeLayoutImporter.ResolveDesignFilePath("ProbeTheme"), Is.EqualTo(designPath));
                Assert.That(Ao2ThemeLayoutImporter.ImportTheme("ProbeTheme"), Is.Not.Null);
                Assert.That(Ao2ThemeLayoutImporter.ResolveDesignFilePath("NoSuchTheme"), Is.Null);
            }
            finally
            {
                Globals.BaseFolders = originalBaseFolders;
                Directory.Delete(baseFolder, recursive: true);
            }
        }

        [Test]
        public void Translate_MapsTheAo2ListsToTheInWindowPanelsAndTheirSwitch()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700",

                // A rectangle under this name describes nothing AO2 draws: `area_list` is only a font key.
                ["area_list"] = "266, 494, 224, 175",
                ["music_list"] = "50, 73, 182, 209",
                ["switch_area_music"] = "177, 49, 56, 23"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);
            double shift = Ao2ThemeLayoutImporter.ClientsStripWidth;

            // BOTH lists occupy AO2's `music_list` rectangle - courtroom.cpp calls
            // set_size_and_pos(ui_area_list, "music_list") - and the A/M button swaps between them.
            Assert.That(result.Layout.Panels["area_list"].Left, Is.EqualTo(50 + shift));
            Assert.That(result.Layout.Panels["area_list"].Width, Is.EqualTo(182));
            Assert.That(result.Layout.Panels["music_list"].Left, Is.EqualTo(50 + shift));
            Assert.That(result.Layout.Panels["music_list"].Top, Is.EqualTo(result.Layout.Panels["area_list"].Top));
            Assert.That(result.ImportedPanelIds, Does.Contain("area_list").And.Contain("music_list"));

            // The A/M switch is hidden by default, and a theme that describes it makes it appear.
            Assert.That(OceanyaPanelCatalog.IsHiddenByDefault("bar_button_areamusic"), Is.True);
            Assert.That(result.Layout.Panels["bar_button_areamusic"].IsHidden, Is.False);
            Assert.That(result.Layout.Panels["bar_button_areamusic"].Left, Is.EqualTo(177 + shift));

            // The buttons that opened the popups have no place in a theme that draws the lists.
            Assert.That(result.Layout.Panels["bar_button_area"].Left, Is.LessThan(shift));
            Assert.That(result.Layout.Panels["bar_button_music"].Left, Is.LessThan(shift));
        }

        [Test]
        public void Translate_KeepsEveryStripPanelInsideTheSurface()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            // The strip reserves room for everything stacked below the clients list. A fixed reservation was
            // why the last few controls were pushed off the bottom edge, out of reach.
            foreach (KeyValuePair<string, OceanyaPanelPlacementState> pair in result.Layout.Panels)
            {
                if (pair.Value.IsHidden || pair.Value.Left >= Ao2ThemeLayoutImporter.ClientsStripWidth)
                {
                    continue;
                }

                Assert.That(
                    pair.Value.Top + pair.Value.Height,
                    Is.LessThanOrEqualTo(result.SurfaceHeight),
                    $"{pair.Key} is stacked past the bottom of the window.");
            }
        }

        [Test]
        public void Translate_ParksTheOffsetButtonInTheStripRatherThanFollowingTheTheme()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700",
                ["pair_offset_spinbox"] = "1001, 49, 59, 25"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            // AO2 only shows that spinbox while the pairing panel is open, so its placement means nothing
            // for a control Oceanya shows all the time.
            Assert.That(result.Layout.Panels["ic_button_offset"].Left, Is.LessThan(Ao2ThemeLayoutImporter.ClientsStripWidth));
            Assert.That(result.Layout.Panels["ic_button_offset"].IsHidden, Is.False);
        }

        [Test]
        public void Translate_UsesTheMusicDisplayAsTheOocHeaderOnlyWhenItIsShapedLikeOne()
        {
            // AO2's default theme: a 26px bar directly above the server chatlog, same x, same width.
            Dictionary<string, string> asHeader = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 714, 579",
                ["server_chatlog"] = "490, 1, 224, 277",
                ["music_display"] = "490, 0, 224, 26",
                ["music_name"] = "0, 0, 224, 26"
            };

            Ao2ThemeImportResult header = Ao2ThemeLayoutImporter.Translate(asHeader);
            double shift = Ao2ThemeLayoutImporter.ClientsStripWidth;

            Assert.That(header.Layout.Panels["ooc_stream_backdrop"].IsHidden, Is.False);
            Assert.That(header.Layout.Panels["ooc_stream_backdrop"].Left, Is.EqualTo(490 + shift));
            Assert.That(header.Layout.Panels["ooc_stream_backdrop"].Height, Is.EqualTo(26));

            // music_name is relative to the display, so it lands inside it rather than at 0,0.
            Assert.That(header.Layout.Panels["ooc_stream_text"].Left, Is.EqualTo(490 + shift));
            Assert.That(header.Layout.Panels["ooc_stream_text"].Top, Is.EqualTo(0));

            // The OOC chat keeps the whole chatlog rectangle when the theme owns the header.
            Assert.That(header.Layout.Panels["ooc_chat"].Height, Is.EqualTo(277));

            // A theme can also repurpose the widget as a window-sized decoration, which is not a header.
            Dictionary<string, string> asDecoration = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700",
                ["server_chatlog"] = "1001, 50, 211, 211",
                ["music_display"] = "0, 0, 1262, 700",
                ["music_name"] = "103, 21, 130, 23"
            };

            Ao2ThemeImportResult decoration = Ao2ThemeLayoutImporter.Translate(asDecoration);

            // Our own header bar has no counterpart there, so it is hidden rather than invented.
            Assert.That(decoration.Layout.Panels["ooc_stream_backdrop"].IsHidden, Is.True);
            Assert.That(decoration.Layout.Panels["ooc_stream_text"].IsHidden, Is.False);
        }

        [Test]
        public void ResolveThemeImage_ProbesEveryExtensionInTheThemeBeforeTheDefaultTheme()
        {
            string baseFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string themeFolder = Path.Combine(baseFolder, "themes", "ExtTheme");
            string defaultFolder = Path.Combine(baseFolder, "themes", "default");
            Directory.CreateDirectory(themeFolder);
            Directory.CreateDirectory(defaultFolder);

            // The theme's own art uses a LATER extension than the default theme's, which is exactly the
            // case that used to resolve to the default theme (AO2 probes one theme at a time).
            File.WriteAllBytes(Path.Combine(themeFolder, "holdit.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(defaultFolder, "holdit.gif"), new byte[] { 1 });

            List<string> originalBaseFolders = Globals.BaseFolders?.ToList() ?? new List<string>();
            try
            {
                Globals.BaseFolders = new List<string> { baseFolder };

                Assert.That(
                    Ao2ThemeLayoutImporter.ResolveThemeImage("ExtTheme", "holdit"),
                    Is.EqualTo(Path.Combine(themeFolder, "holdit.png")));

                // Missing in the theme still falls back to the default theme.
                Assert.That(
                    Ao2ThemeLayoutImporter.ResolveThemeImage("ExtTheme", "objection"),
                    Is.Null);
            }
            finally
            {
                Globals.BaseFolders = originalBaseFolders;
                Directory.Delete(baseFolder, recursive: true);
            }
        }

        [Test]
        public void ApplyThemeAppearance_UsesTheThemesArtFontsAndBackdrop()
        {
            string baseFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string themeFolder = Path.Combine(baseFolder, "themes", "ArtTheme");
            Directory.CreateDirectory(themeFolder);
            File.WriteAllText(Path.Combine(themeFolder, "courtroom_design.ini"), "courtroom = 0, 0, 714, 668");
            File.WriteAllBytes(Path.Combine(themeFolder, "holdit.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(themeFolder, "holdit_selected.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(themeFolder, "courtroombackground.png"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(themeFolder, "courtroom_fonts.ini"), string.Join("\n", new[]
            {
                "ic_chatlog = 13",
                "ic_chatlog_font = Consolas",
                "ic_chatlog_bold = 1",
                "ic_chatlog_color = 255, 128, 0"
            }));

            List<string> originalBaseFolders = Globals.BaseFolders?.ToList() ?? new List<string>();
            try
            {
                Globals.BaseFolders = new List<string> { baseFolder };
                Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.ImportTheme("ArtTheme")!.Value;
                OceanyaThemeLayoutState layout = result.Layout;

                // Art: AO2's own image names, with the *_selected variant as the checked art.
                Assert.That(layout.Panels["shout_holdit"].ImagePath, Does.EndWith("holdit.png"));
                Assert.That(layout.Panels["shout_holdit"].CheckedImagePath, Does.EndWith("holdit_selected.png"));

                // Typography from courtroom_fonts.ini, colour converted from AO2's r, g, b form.
                // AO2 font sizes are Qt POINT sizes; WPF's are device independent pixels (13pt = 17.33px).
            Assert.That(layout.Panels["ic_log"].FontSize, Is.EqualTo(17.33).Within(0.01));
                Assert.That(layout.Panels["ic_log"].FontFamily, Is.EqualTo("Consolas"));
                Assert.That(layout.Panels["ic_log"].IsBold, Is.True);
                Assert.That(layout.Panels["ic_log"].TextColor, Is.EqualTo("#FFFF8000"));

                // The backdrop arrives as a locked, lowest-z image panel occupying the theme's own
                // rectangle - not a stretched surface fill, which would not line up with the widgets.
                OceanyaCustomPanelDefinition backdrop = layout.CustomPanels.Single(definition =>
                    definition.Id == Ao2ThemeLayoutImporter.ThemeBackdropPanelId);
                Assert.That(backdrop.ImagePath, Does.EndWith("courtroombackground.png"));
                Assert.That(backdrop.Placement.Left, Is.EqualTo(Ao2ThemeLayoutImporter.ClientsStripWidth));
                Assert.That(backdrop.Placement.Width, Is.EqualTo(714));
                Assert.That(backdrop.Placement.Height, Is.EqualTo(668));
                Assert.That(backdrop.Placement.IsLocked, Is.True, "A full-size backdrop must not be grabbable.");
                Assert.That(backdrop.Placement.ZOrder, Is.LessThan(0), "It has to sit behind every panel.");
            }
            finally
            {
                Globals.BaseFolders = originalBaseFolders;
                Directory.Delete(baseFolder, recursive: true);
            }
        }

        [Test]
        public void Translate_KeepsTheOocBlockTogether()
        {
            Dictionary<string, string> design = new Dictionary<string, string>
            {
                ["courtroom"] = "0, 0, 1262, 700",
                ["server_chatlog"] = "1001, 50, 211, 211"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);
            double shift = Ao2ThemeLayoutImporter.ClientsStripWidth;

            // AO2 describes the OOC block as one rectangle; our split panels have to land inside it
            // instead of staying at their Oceanya defaults.
            OceanyaPanelPlacementState background = result.Layout.Panels["ooc_log"];
            OceanyaPanelPlacementState header = result.Layout.Panels["ooc_stream_backdrop"];
            OceanyaPanelPlacementState headerText = result.Layout.Panels["ooc_stream_text"];
            OceanyaPanelPlacementState chat = result.Layout.Panels["ooc_chat"];

            Assert.That(background.Left, Is.EqualTo(1001 + shift));
            Assert.That(headerText.Left, Is.EqualTo(background.Left));
            Assert.That(chat.Left, Is.EqualTo(background.Left));
            Assert.That(headerText.Top, Is.EqualTo(background.Top));
            Assert.That(chat.Top, Is.EqualTo(background.Top + headerText.Height));

            // Our own header bar has no AO2 counterpart unless the theme's music display is shaped like one,
            // so it is hidden rather than invented; the label still gets the strip along the log's top edge.
            Assert.That(header.IsHidden, Is.True);
            Assert.That(chat.Top + chat.Height, Is.EqualTo(background.Top + background.Height).Within(0.5));
            Assert.That(chat.IsHidden, Is.False);
        }

        [Test]
        public void ConvertRgbTripletToHex_MatchesAo2ColourForm()
        {
            Assert.That(Ao2ThemeLayoutImporter.ConvertRgbTripletToHex("0, 0, 0"), Is.EqualTo("#FF000000"));
            Assert.That(Ao2ThemeLayoutImporter.ConvertRgbTripletToHex("210, 210, 0"), Is.EqualTo("#FFD2D200"));
            Assert.That(Ao2ThemeLayoutImporter.ConvertRgbTripletToHex("not a colour"), Is.Null);
        }

        [Test]
        public void ParseDesignFile_MissingFileYieldsNoEntries()
        {
            Assert.That(Ao2ThemeLayoutImporter.ParseDesignFile("does-not-exist.ini"), Is.Empty);
        }
    }
}
