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
        private static readonly string[] ImperativeRuleVerbs = new[]
        {
            "respond",
            "reply",
            "speak",
            "talk",
            "write",
            "answer",
            "use",
            "avoid",
            "stay",
            "be",
            "keep",
            "continue",
            "stop"
        };
        private static readonly string[] MetaRuleMentions = new[]
        {
            "example",
            "for example",
            "test sentence",
            "test phrase",
            "sample sentence",
            "sample phrase",
            "contains the word",
            "contains the phrase",
            "if i say",
            "what if i say",
            "saying the word",
            "mentioning the word"
        };

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
        /// Phrases that indicate a player is issuing a session-scoped instruction.
        /// </summary>
        private static readonly string[] SessionInstructionCues = new[]
        {
            "for this session",
            "this session",
            "until reconnect",
            "until restart",
            "until we reset",
            "for the rest of this session"
        };

        /// <summary>
        /// Phrases that indicate a player is revoking or overriding a persistent rule.
        /// </summary>
        private static readonly string[] RevocationCues = new[]
        {
            "stop doing that",
            "forget that rule",
            "ignore previous rule",
            "ignore that rule",
            "remove that rule",
            "stop following that rule",
            "don't do that anymore",
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
        /// Checks whether a player message contains a session-scoped instruction cue.
        /// </summary>
        public static bool ContainsSessionInstructionCue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            foreach (string cue in SessionInstructionCues)
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
        /// Determines whether a player message should be promoted into a standing rule.
        /// </summary>
        public static bool ShouldPromoteRuleCommand(string message, out string scope)
        {
            scope = DetermineRuleScope(message);
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant().Trim();
            bool hasCue = ContainsDurableInstructionCue(lower) || ContainsSessionInstructionCue(lower);
            if (!hasCue || !HasLeadingPromotionCue(lower) || LooksLikeMetaMention(lower))
            {
                return false;
            }

            return LooksImperativeRule(lower);
        }

        /// <summary>
        /// Determines whether the instruction should persist for the whole session or beyond.
        /// </summary>
        public static string DetermineRuleScope(string message)
        {
            return ContainsSessionInstructionCue(message) ? "session" : "persistent";
        }

        /// <summary>
        /// Adds a new persistent rule.
        /// </summary>
        public void AddRule(string text, string kind = "behavior", string source = "player_command", string? scope = null)
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
                    Source = source,
                    Scope = string.IsNullOrWhiteSpace(scope) ? DetermineRuleScope(text) : scope.Trim()
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

        private static bool LooksImperativeRule(string lowerMessage)
        {
            if (string.IsNullOrWhiteSpace(lowerMessage))
            {
                return false;
            }

            return ImperativeRuleVerbs.Any(verb =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    lowerMessage,
                    $@"\b{System.Text.RegularExpressions.Regex.Escape(verb)}\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                || lowerMessage.IndexOf("you ", StringComparison.OrdinalIgnoreCase) >= 0
                || lowerMessage.IndexOf("your ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasLeadingPromotionCue(string lowerMessage)
        {
            if (string.IsNullOrWhiteSpace(lowerMessage))
            {
                return false;
            }

            string normalized = lowerMessage.TrimStart();
            string[] politePrefixes = { "please ", "pls ", "okay ", "ok ", "hey " };
            foreach (string prefix in politePrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length).TrimStart();
                    break;
                }
            }

            IEnumerable<string> allCues = DurableInstructionCues.Concat(SessionInstructionCues);
            return allCues.Any(cue => normalized.StartsWith(cue, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeMetaMention(string lowerMessage)
        {
            if (string.IsNullOrWhiteSpace(lowerMessage))
            {
                return false;
            }

            if (MetaRuleMentions.Any(token => lowerMessage.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return System.Text.RegularExpressions.Regex.IsMatch(
                lowerMessage,
                @"\b(?:example|sample|test)\b.{0,40}[""`]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
