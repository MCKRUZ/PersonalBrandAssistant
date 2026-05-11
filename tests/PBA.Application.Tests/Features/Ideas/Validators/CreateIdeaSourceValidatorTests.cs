using FluentValidation.TestHelper;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Validators;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Validators;

public class CreateIdeaSourceValidatorTests
{
    private readonly CreateIdeaSourceValidator _validator = new();

    [Fact]
    public void Validate_ValidRssSource_Passes()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Tech Blog",
            Type = IdeaSourceType.RSS,
            FeedUrl = "https://example.com/feed",
            Category = "Tech",
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "",
            Type = IdeaSourceType.RSS,
            FeedUrl = "https://example.com/feed",
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameExceeds200_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = new string('a', 201),
            Type = IdeaSourceType.RSS,
            FeedUrl = "https://example.com/feed",
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_RssWithoutFeedUrl_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Tech Blog",
            Type = IdeaSourceType.RSS,
            FeedUrl = null,
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FeedUrl);
    }

    [Fact]
    public void Validate_NonRssWithoutFeedUrl_Passes()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Manual Source",
            Type = IdeaSourceType.Manual,
            FeedUrl = null,
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.FeedUrl);
    }

    [Fact]
    public void Validate_PollIntervalBelow5_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Source",
            Type = IdeaSourceType.Manual,
            PollIntervalMinutes = 4
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PollIntervalMinutes);
    }

    [Fact]
    public void Validate_PollIntervalAbove1440_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Source",
            Type = IdeaSourceType.Manual,
            PollIntervalMinutes = 1441
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PollIntervalMinutes);
    }

    [Fact]
    public void Validate_PollIntervalInRange_Passes()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Source",
            Type = IdeaSourceType.Manual,
            PollIntervalMinutes = 60
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.PollIntervalMinutes);
    }
}
