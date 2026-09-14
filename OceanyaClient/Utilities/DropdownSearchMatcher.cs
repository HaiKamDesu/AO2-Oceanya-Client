using System;
using System.Collections.Generic;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Shared search semantics for the client's searchable dropdowns: prefix matches first, then
    /// substring matches.
    /// </summary>
    /// <remarks>
    /// Prefix-only filtering makes half the roster unreachable by its own name - typing "saber" finds
    /// nothing when the folder is called "(pb)saber pendragon", and AO character folders are full of
    /// bracketed source tags like that. Substring matching alone would be worse in the other direction:
    /// typing "phoenix" would rank "Hobo_Phoenix" alongside "Phoenix". Ranking prefix hits ahead of
    /// substring hits keeps the familiar "type the start of the name" behaviour and still finds the rest.
    ///
    /// Cost is one pass with an ordinal <c>IndexOf</c> per candidate, and the pass stops as soon as enough
    /// prefix matches are found to fill the result, so a 10,000-entry roster does not scan further than it
    /// has to. Nothing is sorted and nothing is allocated per candidate.
    /// </remarks>
    public static class DropdownSearchMatcher
    {
        /// <summary>
        /// Filters and ranks <paramref name="candidates"/> against <paramref name="query"/>.
        /// </summary>
        /// <typeparam name="T">Candidate type.</typeparam>
        /// <param name="candidates">Items to search, in their natural display order.</param>
        /// <param name="query">Search text; empty returns the first <paramref name="maxResults"/> candidates.</param>
        /// <param name="textSelector">Returns the text to match for a candidate.</param>
        /// <param name="maxResults">Upper bound on returned items.</param>
        /// <returns>Prefix matches in source order, followed by substring matches in source order.</returns>
        public static List<T> Filter<T>(
            IEnumerable<T> candidates,
            string? query,
            Func<T, string> textSelector,
            int maxResults)
        {
            List<T> prefixMatches = new List<T>();
            if (candidates == null || maxResults <= 0)
            {
                return prefixMatches;
            }

            string normalizedQuery = query ?? string.Empty;
            if (normalizedQuery.Length == 0)
            {
                foreach (T candidate in candidates)
                {
                    if (prefixMatches.Count >= maxResults)
                    {
                        break;
                    }

                    prefixMatches.Add(candidate);
                }

                return prefixMatches;
            }

            List<T> substringMatches = new List<T>();
            foreach (T candidate in candidates)
            {
                // Enough prefix matches to fill the result on their own: nothing later can outrank them.
                if (prefixMatches.Count >= maxResults)
                {
                    break;
                }

                string text = textSelector(candidate) ?? string.Empty;
                int matchIndex = text.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    continue;
                }

                if (matchIndex == 0)
                {
                    prefixMatches.Add(candidate);
                }
                else if (substringMatches.Count < maxResults)
                {
                    substringMatches.Add(candidate);
                }
            }

            int remaining = maxResults - prefixMatches.Count;
            for (int i = 0; i < substringMatches.Count && i < remaining; i++)
            {
                prefixMatches.Add(substringMatches[i]);
            }

            return prefixMatches;
        }

        /// <summary>
        /// Returns the best single match for <paramref name="query"/>: exact, then prefix, then substring.
        /// </summary>
        /// <typeparam name="T">Candidate type.</typeparam>
        /// <param name="candidates">Items to search, in their natural display order.</param>
        /// <param name="query">Search text.</param>
        /// <param name="textSelector">Returns the text to match for a candidate.</param>
        /// <returns>The best match, or <c>default</c> when nothing matches.</returns>
        public static T? FindBestMatch<T>(IEnumerable<T> candidates, string? query, Func<T, string> textSelector)
        {
            if (candidates == null)
            {
                return default;
            }

            string normalizedQuery = query ?? string.Empty;
            T? prefixMatch = default;
            T? substringMatch = default;
            bool hasPrefixMatch = false;
            bool hasSubstringMatch = false;

            foreach (T candidate in candidates)
            {
                string text = textSelector(candidate) ?? string.Empty;
                if (string.Equals(text, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (normalizedQuery.Length == 0)
                {
                    continue;
                }

                int matchIndex = text.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    continue;
                }

                if (matchIndex == 0)
                {
                    if (!hasPrefixMatch)
                    {
                        prefixMatch = candidate;
                        hasPrefixMatch = true;
                    }
                }
                else if (!hasSubstringMatch)
                {
                    substringMatch = candidate;
                    hasSubstringMatch = true;
                }
            }

            if (hasPrefixMatch)
            {
                return prefixMatch;
            }

            return hasSubstringMatch ? substringMatch : default;
        }
    }
}
