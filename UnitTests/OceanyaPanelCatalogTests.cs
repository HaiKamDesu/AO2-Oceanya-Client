using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Covers the Oceanya theme panel registry and the placement it applies to the main window surface.
    /// </summary>
    [TestFixture]
    public class OceanyaPanelCatalogTests
    {
        [Test]
        public void Catalog_PanelIdsAreUniqueAndStable()
        {
            IReadOnlyList<OceanyaPanelDescriptor> panels = OceanyaPanelCatalog.Panels;

            Assert.That(panels.Select(panel => panel.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(panels.Count),
                "Panel ids are referenced by shared theme files and must be unique.");
            Assert.That(
                panels.Select(panel => panel.Id),
                Is.EquivalentTo(new[] { "ic_log", "ooc_log", "clients_list", "shout_row", "ic_settings", "dredd_row" }),
                "Renaming or removing a panel id breaks existing themes and needs a theme compatibility bump.");
        }

        [Test]
        public void Catalog_EveryPanelHasANameAndUsableMinimums()
        {
            foreach (OceanyaPanelDescriptor panel in OceanyaPanelCatalog.Panels)
            {
                Assert.That(panel.DisplayName, Is.Not.Empty, panel.Id);
                Assert.That(panel.MinimumWidth, Is.GreaterThan(0), panel.Id);
                Assert.That(panel.MinimumHeight, Is.GreaterThan(0), panel.Id);
                Assert.That(panel.Placement.Width, Is.GreaterThanOrEqualTo(panel.MinimumWidth), panel.Id);
                Assert.That(panel.Placement.Height, Is.GreaterThanOrEqualTo(panel.MinimumHeight), panel.Id);
            }
        }

        [Test]
        public void Catalog_DefaultPlacementsMatchTheHistoricFixedLayout()
        {
            // These are the exact Canvas values MainWindow.xaml used before placement became data.
            AssertPlacement("ic_log", 59, 0, 228, 298);
            AssertPlacement("ooc_log", 287, 0, 222, 298);
            AssertPlacement("clients_list", 0, 0, 54, 298);
            AssertPlacement("shout_row", 2, 296, 428, 42);
            AssertPlacement("ic_settings", 0, 343, 509, 260);
            AssertPlacement("dredd_row", 0, 603, 509, 30);

            Assert.That(OceanyaPanelCatalog.Get("ic_log").BackdropPlacement,
                Is.EqualTo(new OceanyaPanelPlacement(55, 0, 232, 323)));
            Assert.That(OceanyaPanelCatalog.Get("ooc_log").BackdropPlacement,
                Is.EqualTo(new OceanyaPanelPlacement(287, 0, 222, 333)));
        }

        [Test]
        public void Get_UnknownPanelIdThrows()
        {
            Assert.That(() => OceanyaPanelCatalog.Get("not_a_panel"), Throws.TypeOf<KeyNotFoundException>());
            Assert.That(OceanyaPanelCatalog.TryGet("not_a_panel"), Is.Null);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplyDefaultPlacements_PlacesPanelAndBackdropOnTheCanvas()
        {
            Border icLog = new Border();
            Border icLogBackdrop = new Border();
            Border clientsList = new Border();
            Canvas canvas = new Canvas();
            canvas.Children.Add(icLogBackdrop);
            canvas.Children.Add(icLog);
            canvas.Children.Add(clientsList);

            int placedCount = OceanyaPanelLayout.ApplyDefaultPlacements(new Dictionary<string, OceanyaPanelElements>
            {
                ["ic_log"] = new OceanyaPanelElements(icLog, icLogBackdrop),
                ["clients_list"] = new OceanyaPanelElements(clientsList),
                ["not_a_panel"] = new OceanyaPanelElements(new Border())
            });

            Assert.That(placedCount, Is.EqualTo(2), "Unknown ids are skipped, not thrown on.");
            Assert.That(Canvas.GetLeft(icLog), Is.EqualTo(59));
            Assert.That(Canvas.GetTop(icLog), Is.EqualTo(0));
            Assert.That(icLog.Width, Is.EqualTo(228));
            Assert.That(icLog.Height, Is.EqualTo(298));
            Assert.That(Canvas.GetLeft(icLogBackdrop), Is.EqualTo(55));
            Assert.That(icLogBackdrop.Height, Is.EqualTo(323));
            Assert.That(Canvas.GetLeft(clientsList), Is.EqualTo(0));
            Assert.That(Canvas.GetTop(clientsList), Is.EqualTo(0));
        }

        private static void AssertPlacement(string id, double left, double top, double width, double height)
        {
            Assert.That(
                OceanyaPanelCatalog.Get(id).Placement,
                Is.EqualTo(new OceanyaPanelPlacement(left, top, width, height)),
                id);
        }
    }
}
