using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class MetadataSourcePreferenceApplier
{
    public static int Apply(
        MetadataReviewSession review,
        MetadataSourcePreferenceProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(review);
        var normalized = MetadataSourcePreferenceProfile.Normalize(profile);
        var appliedCount = 0;
        foreach (var field in Enum.GetValues<MetadataField>())
        {
            if (review.SelectCandidate(field, normalized.GetPreferredSourceName(field)))
            {
                appliedCount++;
            }
        }

        return appliedCount;
    }
}
