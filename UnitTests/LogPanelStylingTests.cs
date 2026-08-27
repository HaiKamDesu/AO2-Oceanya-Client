using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Documents;
using System.Windows.Media;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Components;
using OceanyaClient.Features.Theme;

namespace UnitTests
{
    /// <summary>
    /// Guards that theming a log panel never costs the log its text.
    /// </summary>
    /// <remarks>
    /// An imported theme writes fonts, borders and scrollbar colours onto the IC and OOC logs, and those
    /// reach into the control's own visual tree. A styling change that broke the text host would show up as
    /// "messages arrive but nothing appears", so the round trip is pinned here.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class LogPanelStylingTests
    {
        [Test]
        public void ThemedLogKeepsItsTextThroughApplyAndReset()
        {
            ICLog log = new ICLog();
            Window host = new Window { Width = 320, Height = 240, Content = log, ShowActivated = false };
            host.Show();

            RichTextBox box = (RichTextBox)((ScrollViewer)log.Content).Content;
            box.Document.Blocks.Add(new Paragraph(new Run("hello world")));
            log.UpdateLayout();

            OceanyaPanelDescriptor descriptor = new OceanyaPanelDescriptor(
                "probe_log",
                "Probe Log",
                new OceanyaPanelPlacement(0, 0, 320, 240),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.Static);

            OceanyaPanelPlacementState state = new OceanyaPanelPlacementState
            {
                FontSize = 13.33,
                FontFamily = "Segoe UI",
                TextColor = "#FFFFFFFF",
                BorderThickness = 0,
                ScrollbarTrackColor = "#FFFFFFFF",
                ScrollbarHandleColor = "#FFAEAFAE",
                ScrollbarBorderColor = "#FF282728"
            };

            OceanyaPanelStyleApplier.Apply(descriptor, log, state);
            log.UpdateLayout();

            Assert.That(Text(box), Is.EqualTo("hello world"));
            Assert.That(box.IsVisible, Is.True);
            Assert.That(box.ActualHeight, Is.GreaterThan(0));

            OceanyaPanelStyleApplier.RestoreBaseline(descriptor.Id, log);
            log.UpdateLayout();

            Assert.That(Text(box), Is.EqualTo("hello world"));
            Assert.That(box.IsVisible, Is.True);
            Assert.That(box.FontSize, Is.EqualTo(14), "The log's own font size comes back on reset.");
            Assert.That(((ScrollViewer)log.Content).Content, Is.SameAs(box), "The text host must survive restyling.");

            host.Close();
        }

        [Test]
        public void FontDrivenHeightNeverShrinksAPanelTheThemeSized()
        {
            Grid panel = new Grid { Height = 23 };
            OceanyaPanelDescriptor descriptor = new OceanyaPanelDescriptor(
                "probe_text",
                "Probe Text",
                new OceanyaPanelPlacement(0, 0, 100, 23),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.TextInput);

            OceanyaPanelStyleApplier.Apply(descriptor, panel, new OceanyaPanelPlacementState { FontSize = 8 });

            // In AO2 a widget's geometry comes from the design file and owes nothing to its font, so hugging
            // a small font would clip a panel the theme sized deliberately.
            Assert.That(panel.Height, Is.EqualTo(23));

            OceanyaPanelStyleApplier.RestoreBaseline(descriptor.Id, panel);
            OceanyaPanelStyleApplier.Apply(descriptor, panel, new OceanyaPanelPlacementState { FontSize = 30 });

            // A font too tall for the panel still grows it, so the text is never cut off.
            Assert.That(panel.Height, Is.GreaterThan(23));
        }

        [Test]
        public void ThemedLabelDropsThePaddingThatClippedItsText()
        {
            Label header = new Label
            {
                Content = "[0] Franziska (\"Client1\")",
                Height = 23,
                Width = 130
            };

            Window host = new Window { Width = 200, Height = 100, Content = header, ShowActivated = false };
            host.Show();
            header.UpdateLayout();

            // A WPF Label pads 5px on every side, which a Qt label does not: on a 23px panel with a small
            // theme font that padding clipped the bottom half of the glyphs.
            Assert.That(header.Padding.Top, Is.GreaterThan(0), "WPF's own default is what makes this necessary.");

            OceanyaPanelDescriptor descriptor = new OceanyaPanelDescriptor(
                "probe_header",
                "Probe Header",
                new OceanyaPanelPlacement(0, 0, 130, 23),
                minimumWidth: 10,
                minimumHeight: 10,
                kind: OceanyaPanelKind.TextToggle);

            OceanyaPanelStyleApplier.Apply(descriptor, header, new OceanyaPanelPlacementState { FontSize = 10.67 });
            header.UpdateLayout();

            Assert.That(header.Padding, Is.EqualTo(new Thickness(0)));
            Assert.That(header.Height, Is.EqualTo(23), "The theme's own rectangle is kept.");

            FrameworkElement content = (FrameworkElement)VisualTreeHelperFirstChild(header);
            Assert.That(content.ActualHeight, Is.LessThanOrEqualTo(23));

            OceanyaPanelStyleApplier.RestoreBaseline(descriptor.Id, header);
            Assert.That(header.Padding.Top, Is.GreaterThan(0), "Reset brings the control's own padding back.");
            host.Close();
        }

        private static DependencyObject VisualTreeHelperFirstChild(DependencyObject element)
        {
            return System.Windows.Media.VisualTreeHelper.GetChild(element, 0);
        }

        [Test]
        public void ThemedSliderKeepsItsTrackFrame()
        {
            string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            string art = Path.Combine(folder, "meter.png");
            File.WriteAllBytes(art, System.Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg=="));

            try
            {
                Slider slider = new Slider { Width = 168, Height = 30, Minimum = 0, Maximum = 100, Value = 50 };
                Window host = new Window { Width = 300, Height = 100, Content = slider, ShowActivated = false };
                host.Show();

                OceanyaPanelDescriptor descriptor = new OceanyaPanelDescriptor(
                    "probe_slider",
                    "Probe Slider",
                    new OceanyaPanelPlacement(0, 0, 168, 30),
                    minimumWidth: 10,
                    minimumHeight: 10,
                    kind: OceanyaPanelKind.Slider);

                // Exactly what an import produces: the border colour from `QSlider::sub-page`, and the
                // zero border width every mapped panel gets.
                OceanyaPanelStyleApplier.Apply(descriptor, slider, new OceanyaPanelPlacementState
                {
                    ImagePath = art,
                    IndicatorImagePath = art,
                    FillColor = "#FFFFFFFF",
                    BackgroundColor = "#00000000",
                    BorderColor = "#FF282728",
                    BorderThickness = 0
                });

                slider.UpdateLayout();
                FlushDispatcher();
                slider.UpdateLayout();

                Border? track = FindTrackFrame(slider);
                Assert.That(track, Is.Not.Null, "The slider draws one bordered track across its whole width.");

                // The generic border pass runs deferred and used to clear this frame right after the slider
                // template drew it, which left the track with no outline at all.
                Assert.That(track!.BorderThickness.Left, Is.EqualTo(1));
                Assert.That(((SolidColorBrush)track.BorderBrush).Color, Is.EqualTo(Color.FromRgb(0x28, 0x27, 0x28)));
                Assert.That(track.ActualWidth, Is.EqualTo(168).Within(1));

                host.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static void FlushDispatcher()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static Border? FindTrackFrame(DependencyObject element)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                if (child is Border border && border.BorderThickness.Left > 0)
                {
                    return border;
                }

                Border? nested = FindTrackFrame(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static string Text(RichTextBox box)
        {
            return new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Trim();
        }
    }
}
