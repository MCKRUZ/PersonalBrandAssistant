using FluentValidation;

namespace PBA.Application.Features.BrandRankingProfile.Commands;

public class UpdateBrandRankingProfileValidator : AbstractValidator<UpdateBrandRankingProfile.Command>
{
    public UpdateBrandRankingProfileValidator()
    {
        RuleFor(x => x.Positioning).NotEmpty();
        RuleFor(x => x.AudiencePrimary).NotEmpty();
        RuleFor(x => x.HalfLifeDays).GreaterThan(0);
        RuleFor(x => x.DecayFloor).InclusiveBetween(0, 1);
        RuleFor(x => x.AntiTopicMultiplier).GreaterThan(0);
        RuleFor(x => x.AuthorityBoost).GreaterThan(0);

        // Required so the optimistic-concurrency check can never be silently skipped (R-H3).
        RuleFor(x => x.ConcurrencyToken).NotEmpty();

        RuleFor(x => x.Pillars).NotEmpty().WithMessage("At least one pillar is required.");
        RuleForEach(x => x.Pillars).ChildRules(p =>
        {
            p.RuleFor(pillar => pillar.Weight).InclusiveBetween(0, 1);
            p.RuleFor(pillar => pillar.Name).NotEmpty();
            // The client must assign pillar ids (R-C3 identity); an empty id would collapse two new
            // pillars into one during reconciliation.
            p.RuleFor(pillar => pillar.Id).NotEqual(Guid.Empty);
        });

        // Deliberately NOT validating that weights sum to ~1.0: read-time renormalization (section-08,
        // R-C2b) makes an exact-sum constraint unnecessary and brittle.
    }
}
