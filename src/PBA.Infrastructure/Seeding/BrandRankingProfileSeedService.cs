using Microsoft.EntityFrameworkCore;
using Npgsql;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Seeding;

/// <summary>
/// Seeds the v1 active BrandRankingProfile from planning/brand-strategy/brand-profile-v0.md.
/// Idempotent (no-op if an active profile exists) and race-safe (a concurrent host losing the
/// partial-unique-index race is swallowed, not fatal at startup) — R-L5.
/// </summary>
public sealed class BrandRankingProfileSeedService(IAppDbContext db) : IBrandRankingProfileSeedService
{
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.BrandRankingProfiles.AnyAsync(p => p.IsActive, cancellationToken))
            return 0;

        db.BrandRankingProfiles.Add(BuildV1());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return 1;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another host won the single-active partial-unique-index race; the profile exists. No-op.
            // Any OTHER DbUpdateException is a real failure and propagates (not silently swallowed).
            return 0;
        }
    }

    private static BrandRankingProfile BuildV1()
    {
        var now = DateTimeOffset.UtcNow;
        return new BrandRankingProfile
        {
            Version = 1,
            IsActive = true,
            UpdatedAt = now,
            Positioning = "I show enterprise teams what AI can actually ship - by building it myself.",
            AudiencePrimary =
                "Senior engineers, EMs, and architects at enterprise companies who are frustrated by AI " +
                "hype and want proof, patterns, and shipped artifacts.",
            AudienceSecondary = "Enterprise executives and decision-makers.",
            HalfLifeDays = 7,
            DecayFloor = 0.075,
            AntiTopicMultiplier = 0.1,
            AuthorityBoost = 1.2,
            Pillars =
            [
                new BrandPillar
                {
                    Name = "Agent-Native Architecture", Weight = 0.28, Order = 0,
                    Description = "Harnesses, Agent Skills, MCP, context engineering, agent memory, " +
                        "multi-agent patterns, plus AI economics/tokenomics/cost.",
                },
                new BrandPillar
                {
                    Name = "Enterprise AI Adoption & Governance", Weight = 0.23, Order = 1,
                    Description = "Exec-altitude: strategy, operating model, governance, ROI, the " +
                        "95%-failure framing.",
                },
                new BrandPillar
                {
                    Name = "Agentic SDLC & Eng Transformation", Weight = 0.22, Order = 2,
                    Description = "How engineering orgs actually build with agents; orchestrate-not-" +
                        "implement; training.",
                },
                new BrandPillar
                {
                    Name = "Claude / Anthropic Agent Engineering", Weight = 0.15, Order = 3,
                    Description = "Bringing Anthropic/Claude-Code patterns into the enterprise; Agent " +
                        "Skills, Claude Code architecture, the MD-who-ships-on-Claude position.",
                },
                new BrandPillar
                {
                    Name = "Microsoft Enterprise AI Stack", Weight = 0.12, Order = 4,
                    Description = "Foundry, Copilot, Agent Framework, .NET/C# in enterprise AI.",
                },
            ],
            AuthorityTopics =
            [
                ".NET / C# in enterprise AI",
                "Anthropic Agent Skills / Claude Code architecture",
                "Claude-Code-style agent harnesses",
                "Agent Skills framework",
                "MCP server design",
                "Agentic SDLC tooling",
            ],
            AntiTopics =
            [
                "Digital-human / avatar / talking-head tech",
                "Consumer-AI gossip / product drama",
                "Model-release horse-race / benchmark-leaderboard news",
                "Crypto / web3 / AI-token coins",
                "AGI philosophy / doomerism",
                "Funding-round / VC news with no enterprise-build angle",
                "Pure consumer dev tutorials with no enterprise constraint",
            ],
            VoiceMarkers =
            [
                "Simple enough for execs, technical enough to not be fluff",
                "Contrarian / false-debate debunking",
                "Data > opinion - specific numbers",
                "Every claim has a build behind it (Mollick rule)",
                "No em dashes, no AI-isms, no promotional inflation",
            ],
        };
    }
}
