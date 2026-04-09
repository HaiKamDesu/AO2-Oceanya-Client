using AO2AIBot.Controller;
using NUnit.Framework;

namespace UnitTests
{
    [TestFixture]
    public class PersistentRuleStoreTests
    {
        [Test]
        public void ContainsDurableInstructionCue_FromNowOn_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsDurableInstructionCue(
                "from now on, change your emote based on the vibe of your message"), Is.True);
        }

        [Test]
        public void ContainsDurableInstructionCue_Always_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsDurableInstructionCue(
                "always respond in red text"), Is.True);
        }

        [Test]
        public void ContainsDurableInstructionCue_EveryTime_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsDurableInstructionCue(
                "every time you speak, pick a different emote"), Is.True);
        }

        [Test]
        public void ContainsDurableInstructionCue_NormalChat_ReturnsFalse()
        {
            Assert.That(PersistentRuleStore.ContainsDurableInstructionCue(
                "hey what's up"), Is.False);
        }

        [Test]
        public void ContainsDurableInstructionCue_NeverDo_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsDurableInstructionCue(
                "never use green text"), Is.True);
        }

        [Test]
        public void ContainsRevocationCue_StopDoingThat_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsRevocationCue(
                "stop doing that"), Is.True);
        }

        [Test]
        public void ContainsRevocationCue_ForgetThatRule_ReturnsTrue()
        {
            Assert.That(PersistentRuleStore.ContainsRevocationCue(
                "forget that rule"), Is.True);
        }

        [Test]
        public void ContainsRevocationCue_NormalChat_ReturnsFalse()
        {
            Assert.That(PersistentRuleStore.ContainsRevocationCue(
                "what do you think about this?"), Is.False);
        }

        [Test]
        public void AddRule_StoresRule()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("from now on, change your emote based on the vibe of your message");

            Assert.That(store.ActiveCount, Is.EqualTo(1));
            IReadOnlyList<string> texts = store.GetActiveRuleTexts();
            Assert.That(texts[0], Does.Contain("emote based on the vibe"));
        }

        [Test]
        public void TryRevokeLatestRule_DisablesNewestRule()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("always use red text");
            store.AddRule("every time pick a different emote");

            Assert.That(store.ActiveCount, Is.EqualTo(2));

            bool revoked = store.TryRevokeLatestRule();
            Assert.That(revoked, Is.True);
            Assert.That(store.ActiveCount, Is.EqualTo(1));

            IReadOnlyList<string> remaining = store.GetActiveRuleTexts();
            Assert.That(remaining[0], Does.Contain("red text"));
        }

        [Test]
        public void RevokeRulesMatching_DisablesMatchingRules()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("always use red text");
            store.AddRule("every time pick a different emote");

            int count = store.RevokeRulesMatching("red text");
            Assert.That(count, Is.EqualTo(1));
            Assert.That(store.ActiveCount, Is.EqualTo(1));

            IReadOnlyList<string> remaining = store.GetActiveRuleTexts();
            Assert.That(remaining[0], Does.Contain("emote"));
        }

        [Test]
        public void Clear_RemovesAllRules()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("rule 1");
            store.AddRule("rule 2");
            store.Clear();

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void AddRule_EmptyText_IsIgnored()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("");
            store.AddRule("   ");

            Assert.That(store.Count, Is.EqualTo(0));
        }

        [Test]
        public void GetActiveRules_OrdersByPriorityDescending()
        {
            PersistentRuleStore store = new PersistentRuleStore();
            store.AddRule("low priority rule");
            store.AddRule("high priority rule");

            // Both default to 100, so order by creation time.
            IReadOnlyList<PersistentRule> rules = store.GetActiveRules();
            Assert.That(rules.Count, Is.EqualTo(2));
            Assert.That(rules[0].Text, Does.Contain("low priority"));
        }
    }
}
