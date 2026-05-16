using FluentValidation.TestHelper;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Validators;

public class BatchDismissRequestValidatorTests
{
    private readonly BatchDismissRequestValidator _validator = new();

    [Theory]
    [InlineData(FeedItemType.AgentDraft)]
    [InlineData(FeedItemType.TrendAlert)]
    [InlineData(FeedItemType.AnalyticsHighlight)]
    [InlineData(FeedItemType.IdeaSuggestion)]
    [InlineData(FeedItemType.ApprovalRequest)]
    [InlineData(FeedItemType.SystemNotification)]
    public void Validate_ValidFeedItemType_NoErrors(FeedItemType type)
    {
        var request = new BatchDismissRequest { Type = type };
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_InvalidEnumValue_HasError()
    {
        var request = new BatchDismissRequest { Type = (FeedItemType)999 };
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Type);
    }
}
