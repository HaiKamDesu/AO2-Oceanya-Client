namespace AO2AIBot.Controller
{
    /// <summary>
    /// Represents a persistent behavior rule extracted from player commands.
    /// Stored outside transcript history and injected into the model input each turn.
    /// </summary>
    public sealed class PersistentRule
    {
        /// <summary>
        /// Gets or sets the unique identifier for this rule.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the rule text as stated by the player.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the kind of rule: "behavior", "persona", "constraint".
        /// </summary>
        public string Kind { get; set; } = "behavior";

        /// <summary>
        /// Gets or sets the source of the rule: "player_command" or "system".
        /// </summary>
        public string Source { get; set; } = "player_command";

        /// <summary>
        /// Gets or sets the scope: "session" or "persistent".
        /// </summary>
        public string Scope { get; set; } = "session";

        /// <summary>
        /// Gets or sets the UTC timestamp when the rule was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets a value indicating whether the rule is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the priority (higher = more important). Default is 100.
        /// </summary>
        public int Priority { get; set; } = 100;
    }

    /// <summary>
    /// Manages persistent behavior rules for a single AI agent instance.
    /// Rules are extracted from player commands containing durable instruction cues
    /// and stored outside the transcript so they survive history truncation.
    /// </summary>
    public sealed class PersistentRuleStore
    {
        private readonly List<PersistentRule> rules = new List<PersistentRule>();
        private readonly object sync = new object();

        /// <summary>
        /// Phrases that indicate a player is issuing a durable instruction.
        /// </summary>
        private static readonly string[] DurableInstructionCues = new[]
        {
            "from now on",
            "always",
            "every time",
            "every line",
            "every message",
            "until i say otherwise",
            "until i tell you",
            "never",
            "don't ever",
            "do not ever",
            "for the rest of",
            "for all future",
            "keep doing",
            "continue to",
            "from here on"
        };

        /// <summary>
        /// Phrases that indicate a player is revoking or overriding a persistent rule.
        /// </summary>
        private static readonly string[] RevocationCues = new[]
        {
            "stop doing that",
            "forget that rule",
            "forget that",
            "nevermind",
            "never mind",
            "cancel that",
            "undo that",
            "stop that",
            "don't do that anymore",
            "no longer",
            "from now on instead"
        };

        /// <summary>
        /// Checks whether a player message contains a durable instruction cue.
        /// </summary>
        public static bool ContainsDurableInstructionCue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            foreach (string cue in DurableInstructionCues)
            {
                if (lower.Contains(cue))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether a player message contains a revocation cue.
        /// </summary>
        public static bool ContainsRevocationCue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            foreach (string cue in RevocationCues)
            {
                if (lower.Contains(cue))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a new persistent rule.
        /// </summary>
        public void AddRule(string text, string kind = "behavior", string source = "player_command")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lock (sync)
            {
                rules.Add(new PersistentRule
                {
                    Text = text.Trim(),
                    Kind = kind,
                    Source = source
                });
            }
        }

        /// <summary>
        /// Disables the most recent rule that matches the given text approximately.
        /// </summary>
        public bool TryRevokeLatestRule()
        {
            lock (sync)
            {
                for (int i = rules.Count - 1; i >= 0; i--)
                {
                    if (rules[i].Enabled)
                    {
                        rules[i].Enabled = false;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Disables all rules matching the given text (case-insensitive substring match).
        /// </summary>
        public int RevokeRulesMatching(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            int count = 0;
            lock (sync)
            {
                foreach (PersistentRule rule in rules)
                {
                    if (rule.Enabled && rule.Text.IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rule.Enabled = false;
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Returns all active rules ordered by priority (descending) then creation time.
        /// </summary>
        public IReadOnlyList<PersistentRule> GetActiveRules()
        {
            lock (sync)
            {
                return rules
                    .Where(r => r.Enabled)
                    .OrderByDescending(r => r.Priority)
                    .ThenBy(r => r.CreatedUtc)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Returns the active rule texts for prompt injection.
        /// </summary>
        public IReadOnlyList<string> GetActiveRuleTexts()
        {
            return GetActiveRules().Select(r => r.Text).ToList().AsReadOnly();
        }

        /// <summary>
        /// Clears all rules (session reset).
        /// </summary>
        public void Clear()
        {
            lock (sync)
            {
                rules.Clear();
            }
        }

        /// <summary>
        /// Gets the total number of rules (active and inactive).
        /// </summary>
        public int Count
        {
            get
            {
                lock (sync)
                {
                    return rules.Count;
                }
            }
        }

        /// <summary>
        /// Gets the number of active rules.
        /// </summary>
        public int ActiveCount
        {
            get
            {
                lock (sync)
                {
                    return rules.Count(r => r.Enabled);
                }
            }
        }
    }
}
