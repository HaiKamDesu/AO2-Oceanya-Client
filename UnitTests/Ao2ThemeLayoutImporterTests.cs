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
            Assert.That(result.SurfaceHeight, Is.EqualTo(700));
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
            Assert.That(result.SurfaceHeight, Is.EqualTo(600));
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
                ["defense_bar"] = "283, 412, 141, 15",
                ["evidence_button"] = "394, 20, 71, 25"
            };

            Ao2ThemeImportResult result = Ao2ThemeLayoutImporter.Translate(design);

            Assert.That(result.UnmappedIdentifiers, Does.Contain("defense_bar"));
            Assert.That(result.UnmappedIdentifiers, Does.Contain("evidence_button"));
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
        public void ParseDesignFile_MissingFileYieldsNoEntries()
        {
            Assert.That(Ao2ThemeLayoutImporter.ParseDesignFile("does-not-exist.ini"), Is.Empty);
        }
    }
}
