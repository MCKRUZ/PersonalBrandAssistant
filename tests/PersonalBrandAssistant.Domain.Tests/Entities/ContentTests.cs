using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.Events;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class ContentTests
{
    private static Content CreateDraft() =>
        Content.Create(ContentType.BlogPost, "Test body");

    [Fact]
    public void NewContent_DefaultsToDraftStatus()
    {
        var content = CreateDraft();
        Assert.Equal(ContentStatus.Draft, content.Status);
    }

    [Theory]
    [InlineData(ContentStatus.Draft, ContentStatus.Review, true)]
    [InlineData(ContentStatus.Draft, ContentStatus.Archived, true)]
    [InlineData(ContentStatus.Draft, ContentStatus.Published, false)]
    [InlineData(ContentStatus.Review, ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Review, ContentStatus.Approved, true)]
    [InlineData(ContentStatus.Approved, ContentStatus.Scheduled, true)]
    [InlineData(ContentStatus.Approved, ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Scheduled, ContentStatus.Publishing, true)]
    [InlineData(ContentStatus.Scheduled, ContentStatus.Draft, false)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Published, true)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Failed, true)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Scheduled, true)]
    [InlineData(ContentStatus.Publishing, ContentStatus.Draft, false)]
    [InlineData(ContentStatus.Published, ContentStatus.Archived, true)]
    [InlineData(ContentStatus.Published, ContentStatus.Draft, false)]
    [InlineData(ContentStatus.Failed, ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Failed, ContentStatus.Archived, true)]
    [InlineData(ContentStatus.Failed, ContentStatus.Publishing, true)]
    [InlineData(ContentStatus.Archived, ContentStatus.Draft, true)]
    [InlineData(ContentStatus.Archived, ContentStatus.Published, false)]
    public void TransitionTo_ValidatesStateTransitions(
        ContentStatus from, ContentStatus to, bool shouldSucceed)
    {
        var content = CreateDraft();
        TransitionToState(content, from);

        if (shouldSucceed)
        {
            content.TransitionTo(to);
            Assert.Equal(to, content.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => content.TransitionTo(to));
        }
    }

    [Fact]
    public void TransitionTo_RaisesContentStateChangedEvent()
    {
        var content = CreateDraft();
        content.TransitionTo(ContentStatus.Review);

        var domainEvent = Assert.Single(content.DomainEvents);
        var stateChanged = Assert.IsType<ContentStateChangedEvent>(domainEvent);
        Assert.Equal(content.Id, stateChanged.ContentId);
        Assert.Equal(ContentStatus.Draft, stateChanged.OldStatus);
        Assert.Equal(ContentStatus.Review, stateChanged.NewStatus);
    }

    [Fact]
    public void Content_WithMultipleTargetPlatforms_StoresCorrectly()
    {
        var platforms = new[] { PlatformType.TwitterX, PlatformType.LinkedIn };
        var content = Content.Create(ContentType.SocialPost, "Post", targetPlatforms: platforms);

        Assert.Equal(2, content.TargetPlatforms.Length);
        Assert.Contains(PlatformType.TwitterX, content.TargetPlatforms);
        Assert.Contains(PlatformType.LinkedIn, content.TargetPlatforms);
    }

    [Fact]
    public void Content_WithEmptyTargetPlatforms_IsValid()
    {
        var content = Content.Create(ContentType.SocialPost, "Post");
        Assert.Empty(content.TargetPlatforms);
    }

    [Fact]
    public void CapturedAutonomyLevel_DefaultsToManual()
    {
        var content = CreateDraft();
        Assert.Equal(AutonomyLevel.Manual, content.CapturedAutonomyLevel);
    }

    [Fact]
    public void CapturedAutonomyLevel_SetViaCreate()
    {
        var content = Content.Create(ContentType.BlogPost, "Test",
            capturedAutonomyLevel: AutonomyLevel.SemiAuto);
        Assert.Equal(AutonomyLevel.SemiAuto, content.CapturedAutonomyLevel);
    }

    [Fact]
    public void RetryCount_DefaultsToZero()
    {
        var content = CreateDraft();
        Assert.Equal(0, content.RetryCount);
    }

    [Fact]
    public void NextRetryAt_IsNullByDefault()
    {
        var content = CreateDraft();
        Assert.Null(content.NextRetryAt);
    }

    [Fact]
    public void PublishingStartedAt_IsNullByDefault()
    {
        var content = CreateDraft();
        Assert.Null(content.PublishingStartedAt);
    }

    [Fact]
    public void ValidTransitions_ExposesAllowedTransitions()
    {
        var transitions = Content.ValidTransitions;
        Assert.NotEmpty(transitions);
        Assert.True(transitions.ContainsKey(ContentStatus.Draft));
    }

    [Theory]
    [InlineData(ContentStatus.Scheduled, ContentStatus.Approved, true)]
    public void TransitionTo_ScheduledToApproved_Succeeds(
        ContentStatus from, ContentStatus to, bool shouldSucceed)
    {
        var content = CreateDraft();
        TransitionToState(content, from);

        if (shouldSucceed)
        {
            content.TransitionTo(to);
            Assert.Equal(to, content.Status);
        }
    }

    private static void TransitionToState(Content content, ContentStatus target)
    {
        if (content.Status == target) return;

        var path = target switch
        {
            ContentStatus.Draft => Array.Empty<ContentStatus>(),
            ContentStatus.Review => new[] { ContentStatus.Review },
            ContentStatus.Approved => new[] { ContentStatus.Review, ContentStatus.Approved },
            ContentStatus.Scheduled => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled },
            ContentStatus.Publishing => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing },
            ContentStatus.Published => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published },
            ContentStatus.Failed => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Failed },
            ContentStatus.Archived => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published, ContentStatus.Archived },
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        foreach (var step in path)
        {
            content.TransitionTo(step);
        }

        content.ClearDomainEvents();
    }
}
