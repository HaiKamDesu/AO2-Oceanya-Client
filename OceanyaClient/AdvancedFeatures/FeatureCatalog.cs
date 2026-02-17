using System.Collections.Generic;

namespace OceanyaClient.AdvancedFeatures
{
    public sealed class FeatureDefinition
    {
        public string FeatureId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public bool SupportsConfiguration { get; }

        public FeatureDefinition(string featureId, string displayName, string description, bool supportsConfiguration)
        {
            FeatureId = featureId;
            DisplayName = displayName;
            Description = description;
            SupportsConfiguration = supportsConfiguration;
        }
    }

    public static class FeatureCatalog
    {
        public static readonly FeatureDefinition DreddBackgroundOverlayOverride =
            new FeatureDefinition(
                AdvancedFeatureIds.DreddBackgroundOverlayOverride,
                "Dredd's Background Overlay Override",
                "Overrides background overlays per position through design.ini [Overlays]. Includes sticky application and rollback cache.",
                supportsConfiguration: true);

        public static IReadOnlyList<FeatureDefinition> Definitions { get; } =
            new List<FeatureDefinition>
            {
                DreddBackgroundOverlayOverride
            };
    }
}
