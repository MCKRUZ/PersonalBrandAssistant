using Microsoft.Extensions.AI;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Tests.Common.Models;

public class AgentModelsTests
{
    [Fact]
    public void AgentTask_CanBeConstructedWithAllProperties()
    {
        var contentId = Guid.NewGuid();
        var parameters = new Dictionary<string, string> { ["topic"] = "AI trends" };
        var task = new AgentTask(AgentCapabilityType.Writer, contentId, parameters);

        Assert.Equal(AgentCapabilityType.Writer, task.Type);
        Assert.Equal(contentId, task.ContentId);
        Assert.Equal("AI trends", task.Parameters["topic"]);
    }

    [Fact]
    public void AgentExecutionResult_CanBeConstructedWithExecutionIdAndOutput()
    {
        var executionId = Guid.NewGuid();
        var output = new AgentOutput { GeneratedText = "Hello world" };
        var result = new AgentExecutionResult(executionId, AgentExecutionStatus.Completed, output, null);

        Assert.Equal(executionId, result.ExecutionId);
        Assert.Equal(AgentExecutionStatus.Completed, result.Status);
        Assert.NotNull(result.Output);
        Assert.Null(result.CreatedContentId);
    }

    [Fact]
    public void AgentOutput_WithCreatesContentTrue_IndicatesContentProducingCapability()
    {
        var output = new AgentOutput
        {
            GeneratedText = "Blog post content",
            Title = "My Blog",
            CreatesContent = true,
        };

        Assert.True(output.CreatesContent);
    }

    [Fact]
    public void AgentOutput_WithCreatesContentFalse_IndicatesDataOnlyCapability()
    {
        var output = new AgentOutput
        {
            GeneratedText = "Analytics summary",
            CreatesContent = false,
        };

        Assert.False(output.CreatesContent);
    }

    [Fact]
    public void AgentOutput_MetadataDefaultsToEmptyDictionary()
    {
        var output = new AgentOutput { GeneratedText = "text" };
        Assert.NotNull(output.Metadata);
        Assert.Empty(output.Metadata);
    }

    [Fact]
    public void AgentOutput_ItemsDefaultsToEmptyList()
    {
        var output = new AgentOutput { GeneratedText = "text" };
        Assert.NotNull(output.Items);
        Assert.Empty(output.Items);
    }

    [Fact]
    public void AgentContext_BundlesContentBrandProfileAndTaskParameters()
    {
        var brandProfile = new BrandProfilePromptModel
        {
            Name = "Test Brand",
            PersonaDescription = "A test persona",
            ToneDescriptors = ["professional"],
            StyleGuidelines = "Be concise",
            PreferredTerms = ["AI"],
            AvoidedTerms = ["synergy"],
            Topics = ["tech"],
            ExampleContent = ["example"],
        };

        var contentModel = new ContentPromptModel
        {
            Title = "Test Content",
            Body = "Content body",
            ContentType = ContentType.BlogPost,
            Status = ContentStatus.Draft,
            TargetPlatforms = [PlatformType.LinkedIn],
        };

        var context = new AgentContext
        {
            ExecutionId = Guid.NewGuid(),
            BrandProfile = brandProfile,
            Content = contentModel,
            PromptService = Mock.Of<IPromptTemplateService>(),
            ChatClient = Mock.Of<IChatClient>(),
            Parameters = new Dictionary<string, string> { ["key"] = "value" },
            ModelTier = ModelTier.Standard,
        };

        Assert.Equal("Test Brand", context.BrandProfile.Name);
        Assert.Equal("Test Content", context.Content!.Title);
        Assert.Equal("value", context.Parameters["key"]);
    }

    [Fact]
    public void BrandProfilePromptModel_ContainsOnlyPromptSafeFields()
    {
        var model = new BrandProfilePromptModel
        {
            Name = "Brand",
            PersonaDescription = "Desc",
            ToneDescriptors = ["friendly"],
            StyleGuidelines = "Guidelines",
            PreferredTerms = ["term1"],
            AvoidedTerms = ["term2"],
            Topics = ["topic1"],
            ExampleContent = ["example1"],
        };

        var properties = typeof(BrandProfilePromptModel).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("Id", propertyNames);
        Assert.DoesNotContain("CreatedAt", propertyNames);
        Assert.DoesNotContain("UpdatedAt", propertyNames);
    }

    [Fact]
    public void ContentPromptModel_ContainsOnlyPromptSafeFields()
    {
        var model = new ContentPromptModel
        {
            Title = "Title",
            Body = "Body",
            ContentType = ContentType.SocialPost,
            Status = ContentStatus.Draft,
            TargetPlatforms = [],
        };

        var properties = typeof(ContentPromptModel).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("Id", propertyNames);
        Assert.DoesNotContain("RetryCount", propertyNames);
        Assert.DoesNotContain("Version", propertyNames);
        Assert.DoesNotContain("NextRetryAt", propertyNames);
    }

    [Fact]
    public void AgentOutputItem_CanBeConstructedWithAllProperties()
    {
        var metadata = new Dictionary<string, string> { ["hashtags"] = "#AI" };
        var item = new AgentOutputItem("Post text", "Post Title", metadata);

        Assert.Equal("Post text", item.Text);
        Assert.Equal("Post Title", item.Title);
        Assert.Equal("#AI", item.Metadata["hashtags"]);
    }
}
