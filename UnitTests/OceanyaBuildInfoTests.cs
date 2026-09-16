using System.IO;
using System.Linq;
using Common;
using NUnit.Framework;

namespace UnitTests
{
    /// <summary>
    /// Covers the build stamp that tells us which source a user's DEBUG.txt came from.
    /// </summary>
    /// <remarks>
    /// These assert the SHAPE of the stamp rather than specific values: the commit and dirty flag are
    /// whatever the machine building the tests happened to be on. The one thing that must never happen is
    /// the stamp throwing or coming back blank, because then a returned log says nothing about its source.
    /// </remarks>
    [TestFixture]
    public class OceanyaBuildInfoTests
    {
        [Test]
        public void EveryFieldHasAValueRatherThanBlankOrNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OceanyaBuildInfo.Version, Is.Not.Empty);
                Assert.That(OceanyaBuildInfo.Commit, Is.Not.Empty);
                Assert.That(OceanyaBuildInfo.Branch, Is.Not.Empty);
                Assert.That(OceanyaBuildInfo.Configuration, Is.Not.Empty);
                Assert.That(OceanyaBuildInfo.BuildTimestampUtc, Is.Not.Empty);
            });
        }

        [Test]
        public void VersionIsTheAppVersionNotAnAssemblyQuad()
        {
            // Directory.Build.props sets InformationalVersion to OceanyaAppVersion, and the "+sourcerevision"
            // suffix the SDK can append is trimmed so the line stays readable.
            Assert.That(OceanyaBuildInfo.Version, Does.Match(@"^\d+\.\d+"));
            Assert.That(OceanyaBuildInfo.Version, Does.Not.Contain("+"));
        }

        /// <summary>
        /// The build stamp is only useful if it actually names a commit, so a build made inside the
        /// repository must never fall back to "unknown".
        /// </summary>
        [Test]
        public void CommitIsStampedWhenBuiltFromTheRepository()
        {
            Assert.That(
                OceanyaBuildInfo.Commit,
                Does.Match("^[0-9a-f]{7,40}$"),
                "A build from the repo must carry a real commit hash; 'unknown' means the MSBuild stamp did not run.");
        }

        [Test]
        public void DirtyFlagIsKnownWhenBuiltFromTheRepository()
        {
            Assert.That(
                OceanyaBuildInfo.IsDirty,
                Is.Not.Null,
                "An unknown dirty state means the porcelain status did not run.");
        }

        [Test]
        public void DescribeBuildNamesVersionCommitAndBuildTime()
        {
            string description = OceanyaBuildInfo.DescribeBuild();

            Assert.Multiple(() =>
            {
                Assert.That(description, Does.Contain(OceanyaBuildInfo.Version));
                Assert.That(description, Does.Contain(OceanyaBuildInfo.Commit));
                Assert.That(description, Does.Contain(OceanyaBuildInfo.Configuration));
                Assert.That(description, Does.Contain("built"));
            });
        }

        /// <summary>
        /// The stamp has to reach the actual file a user sends back, not just the API.
        /// </summary>
        /// <remarks>
        /// Written through the real <see cref="DebugFileLogger.Start"/> path rather than by inspecting the
        /// header builder, because the header is only useful if it survives into DEBUG.txt itself.
        /// </remarks>
        [Test]
        public void DebugLogHeaderNamesTheBuild()
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);

            try
            {
                DebugFileLogger.Start(directory);
                string path = DebugFileLogger.LiveLogPath;
                DebugFileLogger.Stop();

                Assert.That(path, Is.Not.Empty, "The logger did not open a file.");
                string[] lines = File.ReadAllLines(path);
                string? buildLine = lines.FirstOrDefault(line => line.StartsWith("Build:", System.StringComparison.Ordinal));

                Assert.That(buildLine, Is.Not.Null, "DEBUG.txt must name the build it came from.");
                Assert.That(buildLine, Does.Contain(OceanyaBuildInfo.Commit));
                Assert.That(buildLine, Does.Contain(OceanyaBuildInfo.Version));
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>
        /// A dirty build has to SAY so loudly: it is the one case where the commit alone is misleading,
        /// because the binary contains changes that exist nowhere in history.
        /// </summary>
        [Test]
        public void DirtyBuildsAreCalledOutInCapitals()
        {
            string description = OceanyaBuildInfo.DescribeBuild();

            if (OceanyaBuildInfo.IsDirty == true)
            {
                Assert.That(description, Does.Contain("DIRTY"));
            }
            else
            {
                Assert.That(description, Does.Not.Contain("DIRTY"));
            }
        }
    }
}
