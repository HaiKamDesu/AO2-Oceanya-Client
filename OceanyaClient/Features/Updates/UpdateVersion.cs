using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.Updates
{
    public readonly struct UpdateVersion : IComparable<UpdateVersion>, IEquatable<UpdateVersion>
    {
        private readonly int[] parts;

        private UpdateVersion(int[] parts, string normalized)
        {
            this.parts = parts;
            Normalized = normalized;
        }

        public string Normalized { get; }

        public static bool TryParse(string? value, out UpdateVersion version)
        {
            return TryParseForChannel(value, UpdateChannel.Stable, out version);
        }

        public static bool TryParseForChannel(string? value, UpdateChannel channel, out UpdateVersion version)
        {
            version = default;
            string trimmed = value?.Trim() ?? string.Empty;
            if (channel == UpdateChannel.Test)
            {
                if (trimmed.StartsWith("test-", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed["test-".Length..];
                }

                int testSuffixIndex = trimmed.IndexOf("-test.", StringComparison.OrdinalIgnoreCase);
                if (testSuffixIndex >= 0)
                {
                    trimmed = trimmed[..testSuffixIndex];
                }
            }
            else if (trimmed.Contains("-test", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[1..];
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            string[] tokens = trimmed.Split('.');
            if (tokens.Length < 2 || tokens.Length > 4)
            {
                return false;
            }

            List<int> parsed = new List<int>();
            foreach (string token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token)
                    || !token.All(char.IsDigit)
                    || !int.TryParse(token, out int part)
                    || part < 0)
                {
                    return false;
                }

                parsed.Add(part);
            }

            while (parsed.Count < 4)
            {
                parsed.Add(0);
            }

            version = new UpdateVersion(parsed.ToArray(), string.Join(".", tokens.Select(token => int.Parse(token).ToString())));
            return true;
        }

        public int CompareTo(UpdateVersion other)
        {
            for (int index = 0; index < 4; index++)
            {
                int comparison = GetPart(index).CompareTo(other.GetPart(index));
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private int GetPart(int index)
        {
            return parts != null && index >= 0 && index < parts.Length ? parts[index] : 0;
        }

        public bool Equals(UpdateVersion other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object? obj)
        {
            return obj is UpdateVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            for (int index = 0; index < 4; index++)
            {
                hash.Add(GetPart(index));
            }

            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return Normalized ?? "0.0";
        }

        public static bool operator >(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) < 0;
        public static bool operator >=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) >= 0;
        public static bool operator <=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) <= 0;
    }
}
