using System.Collections.Generic;

namespace OceanyaClient.AdvancedFeatures
{
    public sealed class FeatureDefinition
    {
        public string FeatureId { get; }
        public string DisplayName { get; }
        public bool SupportsConfiguration { get; }

        public FeatureDefinition(string featureId, string displayName, bool supportsConfiguration)
        {
            FeatureId = featureId;
            DisplayName = displayName;
            SupportsConfiguration = supportsConfiguration;
        }
    }

    public static class FeatureCatalog
    {
        public static readonly FeatureDefinition DreddBackgroundOverlayOverride =
            new FeatureDefinition(
                AdvancedFeatureIds.DreddBackgroundOverlayOverride,
                "Dredd's Background Overlay Override",
                supportsConfiguration: true);

        public static IReadOnlyList<FeatureDefinition> Definitions { get; } =
            new List<FeatureDefinition>
            {
                DreddBackgroundOverlayOverride
            };
    }
}
