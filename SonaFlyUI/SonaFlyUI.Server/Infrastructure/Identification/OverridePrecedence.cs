using SonaFlyUI.Server.Domain.Entities.Identification;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Per-field precedence between original tags and approved catalog overrides
/// (review backlog U04). Rules, in order:
/// 1. An active override wins over whatever the tags currently say.
/// 2. Audio replacement (a changed acoustic fingerprint digest) marks the
///    override stale for re-review instead of silently keeping or dropping it.
/// 3. A changed whole-file hash alone, for example a tag or artwork edit that
///    leaves the audio fingerprint intact, keeps the override without review.
/// 4. Without an override, the tag value stands.
/// Technical properties, presence, file path, and new artwork always follow
/// the scan; only the approved catalog fields are shielded.
/// </summary>
public sealed record ResolvedCatalogField(string? Value, Guid? ValueId, bool StaleForReview);

public static class OverridePrecedence
{
    public static ResolvedCatalogField Resolve(
        string? tagValue,
        Guid? tagValueId,
        CatalogOverride? activeOverride,
        string? currentAudioDigest)
    {
        if (activeOverride == null)
        {
            return new ResolvedCatalogField(tagValue, tagValueId, StaleForReview: false);
        }

        var audioReplaced = activeOverride.SourceFingerprintDigest != null &&
                            currentAudioDigest != null &&
                            string.Equals(activeOverride.SourceFingerprintDigest, currentAudioDigest, StringComparison.OrdinalIgnoreCase) == false;

        return new ResolvedCatalogField(activeOverride.ValueText, activeOverride.ValueId, audioReplaced);
    }
}

