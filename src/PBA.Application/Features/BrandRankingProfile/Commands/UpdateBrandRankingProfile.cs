using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.BrandRankingProfile.Dtos;
using PBA.Domain.Common;
using DomainBrandPillar = PBA.Domain.Entities.BrandPillar;
using DomainBrandRankingProfile = PBA.Domain.Entities.BrandRankingProfile;

namespace PBA.Application.Features.BrandRankingProfile.Commands;

/// <summary>
/// Updates the active brand ranking profile. The write mode is decided SERVER-SIDE (R-H4): query-time
/// knobs (weights, half-life, floor, multipliers, pillar order) always apply and never bump
/// <c>Version</c>; a definition change (positioning, audience, topics, voice markers, or any pillar
/// add/remove/rename/description edit — detected via the entity's own <c>RequiresVersionBump</c>) bumps
/// <c>Version</c> so the scoring sweep (section-07) re-scores stale ideas. There is no client "mode" flag,
/// so a definition change can never be smuggled in as a non-bumping weights update.
/// </summary>
public static class UpdateBrandRankingProfile
{
    public sealed record Command : IRequest<Result<BrandRankingProfileDto>>
    {
        public string Positioning { get; init; } = "";
        public string AudiencePrimary { get; init; } = "";
        public string? AudienceSecondary { get; init; }
        public double HalfLifeDays { get; init; }
        public double DecayFloor { get; init; }
        public double AntiTopicMultiplier { get; init; }
        public double AuthorityBoost { get; init; }
        public IReadOnlyList<PillarInput> Pillars { get; init; } = [];
        public IReadOnlyList<string> AuthorityTopics { get; init; } = [];
        public IReadOnlyList<string> AntiTopics { get; init; } = [];
        public IReadOnlyList<string> VoiceMarkers { get; init; } = [];
        public string ConcurrencyToken { get; init; } = "";
    }

    public sealed record PillarInput(Guid Id, string Name, string Description, double Weight, int Order);

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<BrandRankingProfileDto>>
    {
        public async Task<Result<BrandRankingProfileDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var profile = await db.BrandRankingProfiles
                .Include(p => p.Pillars)
                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

            if (profile is null)
                return Result<BrandRankingProfileDto>.NotFound("No active brand ranking profile");

            // Fail CLOSED on a missing/garbage token (R-H3): a blank or unparseable token must NOT skip the
            // concurrency check, or a write would silently clobber a concurrent edit. Force EF to compare
            // against the client's value by seeding the change-tracker ORIGINAL value (the Xmin setter is
            // private); a stale token then yields 0 rows updated -> DbUpdateConcurrencyException.
            if (!uint.TryParse(request.ConcurrencyToken, out var token))
                return Result<BrandRankingProfileDto>.ValidationFailure(
                    ["ConcurrencyToken is required and must be a valid token value."]);
            db.SetOriginalValue(profile, nameof(profile.Xmin), token);

            // Decide the mode from the actual diff BEFORE mutating anything (consume section-02's logic).
            var requiresBump = profile.RequiresVersionBump(BuildProposed(request));

            // Query-time knobs ALWAYS apply (regardless of mode) and never bump Version.
            profile.HalfLifeDays = request.HalfLifeDays;
            profile.DecayFloor = request.DecayFloor;
            profile.AntiTopicMultiplier = request.AntiTopicMultiplier;
            profile.AuthorityBoost = request.AuthorityBoost;

            if (requiresBump)
            {
                profile.Positioning = request.Positioning;
                profile.AudiencePrimary = request.AudiencePrimary;
                profile.AudienceSecondary = Normalize(request.AudienceSecondary);
                profile.AuthorityTopics = request.AuthorityTopics.ToList();
                profile.AntiTopics = request.AntiTopics.ToList();
                profile.VoiceMarkers = request.VoiceMarkers.ToList();
                ApplyPillarDefinitions(profile, request.Pillars);
                profile.Version += 1;
            }
            else
            {
                // Weights-only: touch ONLY pillar weight/order, never name/description/topics (R-H4).
                foreach (var input in request.Pillars)
                {
                    var existing = profile.Pillars.FirstOrDefault(p => p.Id == input.Id);
                    if (existing is null) continue;
                    existing.Weight = input.Weight;
                    existing.Order = input.Order;
                }
            }

            profile.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<BrandRankingProfileDto>.Conflict(
                    "The brand ranking profile was modified by someone else. Reload and retry.");
            }

            return BrandRankingProfileMapping.ToDto(profile);
        }

        // A detached projection of the request used only to ask the entity whether a definition changed.
        // Weight/Order are query-time and don't affect the decision, so they are left at defaults here.
        // Canonicalize blank optional text to null so a "" vs null difference never flips the version-bump
        // decision (and never triggers a needless re-score sweep) on a semantically-unchanged field.
        private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static DomainBrandRankingProfile BuildProposed(Command request) => new()
        {
            Positioning = request.Positioning,
            AudiencePrimary = request.AudiencePrimary,
            AudienceSecondary = Normalize(request.AudienceSecondary),
            AuthorityTopics = request.AuthorityTopics.ToList(),
            AntiTopics = request.AntiTopics.ToList(),
            VoiceMarkers = request.VoiceMarkers.ToList(),
            Pillars = request.Pillars
                .Select(p => new DomainBrandPillar { Id = p.Id, Name = p.Name, Description = p.Description })
                .ToList()
        };

        private static void ApplyPillarDefinitions(DomainBrandRankingProfile profile, IReadOnlyList<PillarInput> incoming)
        {
            var incomingIds = incoming.Select(p => p.Id).ToHashSet();
            profile.Pillars.RemoveAll(p => !incomingIds.Contains(p.Id));

            foreach (var input in incoming)
            {
                var existing = profile.Pillars.FirstOrDefault(p => p.Id == input.Id);
                if (existing is null)
                {
                    profile.Pillars.Add(new DomainBrandPillar
                    {
                        Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id,
                        Name = input.Name, Description = input.Description,
                        Weight = input.Weight, Order = input.Order
                        // DescriptionEmbedding stays null -> the embedding service embeds it next sweep.
                    });
                    continue;
                }

                // A description edit invalidates the stored pillar vector; null it so section-06 re-embeds.
                if (existing.Description != input.Description)
                    existing.DescriptionEmbedding = null;

                existing.Name = input.Name;
                existing.Description = input.Description;
                existing.Weight = input.Weight;
                existing.Order = input.Order;
            }
        }
    }
}
