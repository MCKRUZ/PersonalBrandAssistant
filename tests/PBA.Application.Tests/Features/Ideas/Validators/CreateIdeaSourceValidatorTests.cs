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
            Type = IdeaSourceType.API,
            FeedUrl = null,
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.FeedUrl);
    }

    [Fact]
    public void Validate_GitHubWithoutApiUrl_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "GitHub Releases",
            Type = IdeaSourceType.GitHub,
            ApiUrl = null,
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ApiUrl);
    }

    [Fact]
    public void Validate_GitHubWithApiUrl_Passes()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "GitHub Releases",
            Type = IdeaSourceType.GitHub,
            ApiUrl = "github:repo:dotnet/runtime",
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.ApiUrl);
    }

    [Fact]
    public void Validate_HackerNewsWithoutUrls_Passes()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "HN Front Page",
            Type = IdeaSourceType.HackerNews,
            FeedUrl = null,
            ApiUrl = null,
            PollIntervalMinutes = 30
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.FeedUrl);
        result.ShouldNotHaveValidationErrorFor(x => x.ApiUrl);
    }

    [Fact]
    public void Validate_PollIntervalBelow5_Fails()
    {
        var command = new CreateIdeaSource.Command
        {
            Name = "Source",
            Type = IdeaSourceType.API,
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
            Type = IdeaSourceType.API,
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
            Type = IdeaSourceType.API,
            PollIntervalMinutes = 60
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.PollIntervalMinutes);
    }
}
