using SonaFlyUI.Server.Domain.Entities.Identification;

namespace SonaFlyUI.Server.Application.Identification;

/// <summary>
/// Enforces the milestone gates from review backlog U07. The recording-only
/// preview milestone may propose title and artist from a recording decision,
/// but it must label release identity unresolved and must not carry
/// exact-edition fields. Album, track and disc positions, and year need both
/// an approved release candidate and the full-resolution milestone flag.
/// Genre and file paths are never proposal fields: genre is subjective and
/// paths change only in the source-file phase.
/// </summary>
public static class ProposalValidator
{
    private static readonly string[] RecordingOnlyFields = ["Title", "Artist"];

    private static readonly string[] EditionFields = ["Album", "TrackNumber", "DiscNumber", "Year"];

    public static IReadOnlyList<string> Validate(MetadataProposal proposal, bool exactReleaseApplyEnabled)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(proposal.ConcurrencyToken))
        {
            errors.Add("A proposal needs a concurrency token for optimistic approval.");
        }

        var fields = proposal.FieldMask
            .Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (fields.Length == 0)
        {
            errors.Add("A proposal must name at least one approved field.");
        }

        foreach (var field in fields)
        {
            if (string.Equals(field, "Genre", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field, "FilePath", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(field + " is never an automatic proposal field.");
            }
            else if (EditionFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                if (proposal.ReleaseCandidateId == null)
                {
                    errors.Add(field + " needs an approved release decision, not just a recording match.");
                }

                if (exactReleaseApplyEnabled == false)
                {
                    errors.Add(field + " is available only in the full identification milestone.");
                }
            }
            else if (RecordingOnlyFields.Contains(field, StringComparer.OrdinalIgnoreCase) == false)
            {
                errors.Add("Unknown proposal field: " + field + ".");
            }
        }

        return errors;
    }
}

