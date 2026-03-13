using FluentValidation.TestHelper;
using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;

namespace PersonalBrandAssistant.Application.Tests.Features.Content.Validators;

public class ListContentQueryValidatorTests
{
    private readonly ListContentQueryValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void Validate_InvalidPageSize_HasError(int pageSize)
    {
        var query = new ListContentQuery(PageSize: pageSize);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    public void Validate_ValidPageSize_NoErrors(int pageSize)
    {
        var query = new ListContentQuery(PageSize: pageSize);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
