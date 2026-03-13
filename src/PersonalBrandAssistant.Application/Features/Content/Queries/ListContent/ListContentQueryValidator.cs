using FluentValidation;

namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;

public sealed class ListContentQueryValidator : AbstractValidator<ListContentQuery>
{
    public ListContentQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}
