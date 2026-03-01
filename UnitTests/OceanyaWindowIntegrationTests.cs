using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class OceanyaWindowIntegrationTests
    {
        private static readonly HashSet<string> AllowedWindowClassNames = new(StringComparer.Ordinal)
        {
            "GenericOceanyaWindow",
            "WaitForm",
            "LoadingScreen"
        };

        [SetUp]
        public void SetUp()
        {
            if (Application.Current == null)
            {
                _ = new Application();
            }
        }

        [Test]
        public void OceanyaClient_XamlWindows_AreRestrictedToAllowedShellWindows()
        {
            string formsDirectory = Path.Combine(GetRepositoryRoot(), "OceanyaClient", "Components", "Forms");
            Regex classRegex = new Regex("x:Class\\s*=\\s*\"OceanyaClient\\.(?<name>[A-Za-z0-9_]+)\"", RegexOptions.Compiled);
            List<string> offenders = new List<string>();

            foreach (string xamlFile in Directory.EnumerateFiles(formsDirectory, "*.xaml", SearchOption.AllDirectories))
            {
                string firstNonEmptyLine = File.ReadLines(xamlFile).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
                if (!firstNonEmptyLine.TrimStart().StartsWith("<Window", StringComparison.Ordinal))
                {
                    continue;
                }

                string source = File.ReadAllText(xamlFile);
                Match match = classRegex.Match(source);
                if (!match.Success)
                {
                    offenders.Add(Path.GetFileName(xamlFile) + " (missing x:Class)");
                    continue;
                }

                string className = match.Groups["name"].Value;
                if (!AllowedWindowClassNames.Contains(className))
                {
                    offenders.Add(Path.GetFileName(xamlFile) + " -> " + className);
                }
            }

            Assert.That(offenders, Is.Empty, "Unexpected XAML Window roots:\n" + string.Join(Environment.NewLine, offenders));
        }

        [Test]
        public void OceanyaClient_WindowClassDeclarations_AreRestrictedToAllowedShellWindows()
        {
            string oceanyaClientDirectory = Path.Combine(GetRepositoryRoot(), "OceanyaClient");
            Regex declarationRegex = new Regex(
                "class\\s+(?<name>[A-Za-z0-9_]+)\\s*:\\s*Window\\b",
                RegexOptions.Compiled);
            List<string> offenders = new List<string>();

            foreach (string sourceFile in Directory.EnumerateFiles(oceanyaClientDirectory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(sourceFile);
                foreach (Match match in declarationRegex.Matches(source))
                {
                    string className = match.Groups["name"].Value;
                    if (AllowedWindowClassNames.Contains(className))
                    {
                        continue;
                    }

                    offenders.Add(Path.GetFileName(sourceFile) + " -> " + className);
                }
            }

            Assert.That(offenders, Is.Empty, "Unexpected ': Window' classes:\n" + string.Join(Environment.NewLine, offenders));
        }

        [Test]
        public void OceanyaClient_DoesNotInstantiateRawWindowConstructors()
        {
            string oceanyaClientDirectory = Path.Combine(GetRepositoryRoot(), "OceanyaClient");
            Regex constructorRegex = new Regex("\\bnew\\s+Window\\b", RegexOptions.Compiled);
            List<string> offenders = new List<string>();

            foreach (string sourceFile in Directory.EnumerateFiles(oceanyaClientDirectory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(sourceFile);
                if (!constructorRegex.IsMatch(source))
                {
                    continue;
                }

                offenders.Add(Path.GetFileName(sourceFile));
            }

            Assert.That(offenders, Is.Empty, "Raw Window constructors found:\n" + string.Join(Environment.NewLine, offenders));
        }

        [Test]
        public void OceanyaWindowManager_SyncsContentSizeAndConstraintsToHostWindow()
        {
            TestHostedContentControl content = new TestHostedContentControl
            {
                Width = 500,
                Height = 320,
                MinWidth = 200,
                MinHeight = 140,
                MaxWidth = 700,
                MaxHeight = 560
            };

            Window hostWindow = OceanyaWindowManager.CreateWindow(content);
            Assert.That(hostWindow, Is.TypeOf<GenericOceanyaWindow>());

            Assert.That(hostWindow.Width, Is.EqualTo(502).Within(0.1));
            Assert.That(hostWindow.Height, Is.EqualTo(352).Within(0.1));
            Assert.That(hostWindow.MinWidth, Is.EqualTo(202).Within(0.1));
            Assert.That(hostWindow.MinHeight, Is.EqualTo(172).Within(0.1));
            Assert.That(hostWindow.MaxWidth, Is.EqualTo(702).Within(0.1));
            Assert.That(hostWindow.MaxHeight, Is.EqualTo(592).Within(0.1));

            content.Width = 640;
            content.Height = 410;
            content.MinWidth = 280;
            content.MinHeight = 180;
            content.MaxWidth = 920;
            content.MaxHeight = 700;

            Assert.That(hostWindow.Width, Is.EqualTo(642).Within(0.1));
            Assert.That(hostWindow.Height, Is.EqualTo(442).Within(0.1));
            Assert.That(hostWindow.MinWidth, Is.EqualTo(282).Within(0.1));
            Assert.That(hostWindow.MinHeight, Is.EqualTo(212).Within(0.1));
            Assert.That(hostWindow.MaxWidth, Is.EqualTo(922).Within(0.1));
            Assert.That(hostWindow.MaxHeight, Is.EqualTo(732).Within(0.1));

            hostWindow.Close();
        }

        [Test]
        public void OceanyaWindowManager_AccountsForBodyMarginInConstraintOffsets()
        {
            TestHostedContentControl content = new TestHostedContentControl
            {
                Width = 400,
                Height = 300,
                MinWidth = 240,
                MinHeight = 180
            };

            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Title = "Test",
                HeaderText = "Test",
                Width = 400,
                Height = 300,
                MinWidth = 240,
                MinHeight = 180,
                BodyMargin = new Thickness(8, 4, 6, 10)
            };

            Window hostWindow = OceanyaWindowManager.CreateWindow(content, options);
            Assert.That(hostWindow, Is.TypeOf<GenericOceanyaWindow>());

            Assert.That(hostWindow.Width, Is.EqualTo(416).Within(0.1));
            Assert.That(hostWindow.Height, Is.EqualTo(346).Within(0.1));
            Assert.That(hostWindow.MinWidth, Is.EqualTo(256).Within(0.1));
            Assert.That(hostWindow.MinHeight, Is.EqualTo(226).Within(0.1));

            hostWindow.Close();
        }

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private sealed class TestHostedContentControl : OceanyaWindowContentControl
        {
            public override string HeaderText => "TEST HOSTED CONTENT";
            public override bool IsUserResizeEnabled => true;
        }
    }
}
