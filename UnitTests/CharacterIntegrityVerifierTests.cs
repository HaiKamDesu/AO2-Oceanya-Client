using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OceanyaClient;

namespace UnitTests
{
    [TestFixture]
    public class CharacterIntegrityVerifierTests
    {
        private string tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "integrity_verifier_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // Best effort.
            }
        }

        [Test]
        public void RunAndPersist_BlankEmotesDefinition_IsDetectedAndFixable()
        {
            string characterDirectory = Path.Combine(tempRoot, "Apollo");
            Directory.CreateDirectory(characterDirectory);
            string charIniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                charIniPath,
                "[Options]\n"
                + "showname=Apollo\n"
                + "emotes=\"\"\n"
                + "[Emotions]\n"
                + "number=2\n"
                + "1=normal#-#normal#0#0\n"
                + "2=talk#-#talk#0#0\n");

            CharacterIntegrityReport report = CharacterIntegrityVerifier.RunAndPersist(characterDirectory, charIniPath, "Apollo");
            CharacterIntegrityIssue? issue = report.Results.FirstOrDefault(result =>
                string.Equals(result.TestName, "Blank emotes Definition", StringComparison.OrdinalIgnoreCase) && !result.Passed);

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.CanAutoFix, Is.True);
            Assert.That(issue.FixActionType, Is.EqualTo(CharacterIntegrityFixActionType.SetBlankEmotesDefinition));

            bool fixedApplied = CharacterIntegrityVerifier.TryApplyFix(report, issue, out _);
            string updatedText = File.ReadAllText(charIniPath);

            Assert.That(fixedApplied, Is.True);
            Assert.That(updatedText, Does.Contain("emotes=2"));
        }

        [Test]
        public void RunAndPersist_MissingAssets_ReportsPerEmoteFailures()
        {
            string characterDirectory = Path.Combine(tempRoot, "Athena");
            Directory.CreateDirectory(characterDirectory);
            string charIniPath = Path.Combine(characterDirectory, "char.ini");
            File.WriteAllText(
                charIniPath,
                "[Options]\n"
                + "showname=Athena\n"
                + "[Emotions]\n"
                + "number=1\n"
                + "1=normal#missing_pre#missing_final#0#0\n");

            CharacterIntegrityReport report = CharacterIntegrityVerifier.RunAndPersist(characterDirectory, charIniPath, "Athena");

            Assert.That(report.HasFailures, Is.True);
            Assert.That(report.Results.Any(result => !result.Passed && result.EmoteId == 1), Is.True);
        }

        [Test]
        public void RunAndPersist_MultipleCharIniFiles_ReportsFailure()
        {
            string characterDirectory = Path.Combine(tempRoot, "Trucy");
            string nestedDirectory = Path.Combine(characterDirectory, "nested");
            Directory.CreateDirectory(characterDirectory);
            Directory.CreateDirectory(nestedDirectory);

            File.WriteAllText(Path.Combine(characterDirectory, "char.ini"), "[Emotions]\nnumber=1\n1=normal#-#normal#0#0\n");
            File.WriteAllText(Path.Combine(nestedDirectory, "char.ini"), "[Emotions]\nnumber=1\n1=normal#-#normal#0#0\n");

            CharacterIntegrityReport report = CharacterIntegrityVerifier.RunAndPersist(characterDirectory);
            CharacterIntegrityIssue? duplicateIssue = report.Results.FirstOrDefault(result =>
                string.Equals(result.TestName, "Multiple char.ini Files", StringComparison.OrdinalIgnoreCase));

            Assert.That(duplicateIssue, Is.Not.Null);
            Assert.That(duplicateIssue!.Passed, Is.False);
        }

        [Test]
        public void Run_NonExistingFolder_ReturnsFailureWithoutThrowing()
        {
            string missingDirectory = Path.Combine(tempRoot, "MissingCharacter");
            CharacterIntegrityReport report = CharacterIntegrityVerifier.Run(missingDirectory);

            Assert.That(report.Results.Count, Is.GreaterThan(0));
            Assert.That(report.Results.Any(result => !result.Passed), Is.True);
        }
    }
}
