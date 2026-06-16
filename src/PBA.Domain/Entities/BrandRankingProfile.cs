namespace PBA.Domain.Entities;

/// <summary>
/// The editable source of truth the feed ranker scores ideas against. Exactly one row is active at a
/// time (enforced by a partial unique index). <see cref="Version"/> bumps only when a DEFINITION field
/// changes (anything the LLM analyzer prompt or pillar embeddings see) — that invalidates stored
/// sub-scores and triggers a re-score sweep. Query-time knobs (weights, half-life, floor, multipliers)
/// change without a bump, so re-weighting re-ranks instantly with zero LLM calls.
/// </summary>
public class BrandRankingProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Version { get; set; }
    public bool IsActive { get; set; }

    // Definition fields (changing any of these bumps Version) ---------------------------------
    public required string Positioning { get; set; }
    public required string AudiencePrimary { get; set; }
    public string? AudienceSecondary { get; set; }
    public List<string> AuthorityTopics { get; set; } = [];
    public List<string> AntiTopics { get; set; } = [];
    public List<string> VoiceMarkers { get; set; } = [];
    public List<BrandPillar> Pillars { get; set; } = [];

    // Query-time ranking knobs (changing these does NOT bump Version) --------------------------
    public double HalfLifeDays { get; set; } = 7;
    public double DecayFloor { get; set; } = 0.075;
    public double AntiTopicMultiplier { get; set; } = 0.1;
    public double AuthorityBoost { get; set; } = 1.2;

    /// <summary>Npgsql system-column (xmin) optimistic-concurrency token; mapped in EF config and set
    /// by EF on load. Section-09 forces a conflict via the change tracker's OriginalValue, not this setter.</summary>
    public uint Xmin { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// True if applying <paramref name="proposed"/> would change a definition field (positioning,
    /// audience, topics, voice markers, or any pillar name/description, or adding/removing a pillar) —
    /// which must bump <see cref="Version"/> and trigger a re-score. Pure; unit-tested in isolation.
    /// Query-time knobs (weights, half-life, floor, multipliers) and pillar Order/Weight do NOT count.
    /// </summary>
    public bool RequiresVersionBump(BrandRankingProfile proposed)
    {
        if (Positioning != proposed.Positioning) return true;
        if (AudiencePrimary != proposed.AudiencePrimary) return true;
        if (AudienceSecondary != proposed.AudienceSecondary) return true;
        // Ordered comparison: these lists are rendered verbatim into the analyzer prompt, so a reorder
        // or duplicate is a real prompt change and must bump (and re-score).
        if (!AuthorityTopics.SequenceEqual(proposed.AuthorityTopics)) return true;
        if (!AntiTopics.SequenceEqual(proposed.AntiTopics)) return true;
        if (!VoiceMarkers.SequenceEqual(proposed.VoiceMarkers)) return true;

        // Pillars: bump on add/remove (by Id) or on any matched pillar's Name/Description change.
        var current = Pillars.ToDictionary(p => p.Id);
        var next = proposed.Pillars.ToDictionary(p => p.Id);
        if (current.Count != next.Count) return true;
        foreach (var (id, currentPillar) in current)
        {
            if (!next.TryGetValue(id, out var proposedPillar)) return true; // removed/replaced id
            if (currentPillar.Name != proposedPillar.Name) return true;
            if (currentPillar.Description != proposedPillar.Description) return true;
        }

        return false;
    }
}
