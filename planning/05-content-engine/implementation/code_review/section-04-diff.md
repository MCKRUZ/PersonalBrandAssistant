diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
new file mode 100644
index 0000000..4a4cde7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
@@ -0,0 +1,8 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IBrandVoiceService
+{
+    Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs
new file mode 100644
index 0000000..af84c38
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IContentPipeline.cs
@@ -0,0 +1,12 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface IContentPipeline
+{
+    Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct);
+    Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct);
+    Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct);
+    Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct);
+    Task<Result<MediatR.Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs b/src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs
new file mode 100644
index 0000000..578d93a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs
@@ -0,0 +1,9 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record BrandVoiceScore(
+    int OverallScore,
+    int ToneAlignment,
+    int VocabularyConsistency,
+    int PersonaFidelity,
+    IReadOnlyList<string> Issues,
+    IReadOnlyList<string> RuleViolations);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/ContentCreationRequest.cs b/src/PersonalBrandAssistant.Application/Common/Models/ContentCreationRequest.cs
new file mode 100644
index 0000000..7d2e331
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/ContentCreationRequest.cs
@@ -0,0 +1,11 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record ContentCreationRequest(
+    ContentType Type,
+    string Topic,
+    string? Outline,
+    PlatformType[]? TargetPlatforms,
+    Guid? ParentContentId,
+    Dictionary<string, string>? Parameters);
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommand.cs
new file mode 100644
index 0000000..dafa8a5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommand.cs
@@ -0,0 +1,13 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;
+
+public sealed record CreateFromTopicCommand(
+    ContentType ContentType,
+    string Topic,
+    string? Outline = null,
+    PlatformType[]? TargetPlatforms = null,
+    Guid? ParentContentId = null,
+    Dictionary<string, string>? Parameters = null) : IRequest<Result<Guid>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandHandler.cs
new file mode 100644
index 0000000..1aa6b7a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandHandler.cs
@@ -0,0 +1,28 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;
+
+public sealed class CreateFromTopicCommandHandler : IRequestHandler<CreateFromTopicCommand, Result<Guid>>
+{
+    private readonly IContentPipeline _pipeline;
+
+    public CreateFromTopicCommandHandler(IContentPipeline pipeline)
+    {
+        _pipeline = pipeline;
+    }
+
+    public async Task<Result<Guid>> Handle(CreateFromTopicCommand request, CancellationToken cancellationToken)
+    {
+        var creationRequest = new ContentCreationRequest(
+            request.ContentType,
+            request.Topic,
+            request.Outline,
+            request.TargetPlatforms,
+            request.ParentContentId,
+            request.Parameters);
+
+        return await _pipeline.CreateFromTopicAsync(creationRequest, cancellationToken);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandValidator.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandValidator.cs
new file mode 100644
index 0000000..33e349d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateFromTopic/CreateFromTopicCommandValidator.cs
@@ -0,0 +1,12 @@
+using FluentValidation;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;
+
+public sealed class CreateFromTopicCommandValidator : AbstractValidator<CreateFromTopicCommand>
+{
+    public CreateFromTopicCommandValidator()
+    {
+        RuleFor(x => x.Topic).NotEmpty().MaximumLength(500);
+        RuleFor(x => x.ContentType).IsInEnum();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommand.cs
new file mode 100644
index 0000000..3ff9dee
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommand.cs
@@ -0,0 +1,6 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;
+
+public sealed record GenerateDraftCommand(Guid ContentId) : IRequest<Result<string>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommandHandler.cs
new file mode 100644
index 0000000..158e64c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateDraft/GenerateDraftCommandHandler.cs
@@ -0,0 +1,20 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;
+
+public sealed class GenerateDraftCommandHandler : IRequestHandler<GenerateDraftCommand, Result<string>>
+{
+    private readonly IContentPipeline _pipeline;
+
+    public GenerateDraftCommandHandler(IContentPipeline pipeline)
+    {
+        _pipeline = pipeline;
+    }
+
+    public Task<Result<string>> Handle(GenerateDraftCommand request, CancellationToken cancellationToken)
+    {
+        return _pipeline.GenerateDraftAsync(request.ContentId, cancellationToken);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommand.cs
new file mode 100644
index 0000000..8a0fc64
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommand.cs
@@ -0,0 +1,6 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;
+
+public sealed record GenerateOutlineCommand(Guid ContentId) : IRequest<Result<string>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommandHandler.cs
new file mode 100644
index 0000000..ec50252
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/GenerateOutline/GenerateOutlineCommandHandler.cs
@@ -0,0 +1,20 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;
+
+public sealed class GenerateOutlineCommandHandler : IRequestHandler<GenerateOutlineCommand, Result<string>>
+{
+    private readonly IContentPipeline _pipeline;
+
+    public GenerateOutlineCommandHandler(IContentPipeline pipeline)
+    {
+        _pipeline = pipeline;
+    }
+
+    public Task<Result<string>> Handle(GenerateOutlineCommand request, CancellationToken cancellationToken)
+    {
+        return _pipeline.GenerateOutlineAsync(request.ContentId, cancellationToken);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommand.cs
new file mode 100644
index 0000000..4892e72
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommand.cs
@@ -0,0 +1,6 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;
+
+public sealed record SubmitForReviewCommand(Guid ContentId) : IRequest<Result<Unit>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommandHandler.cs
new file mode 100644
index 0000000..7b9b5d0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/SubmitForReview/SubmitForReviewCommandHandler.cs
@@ -0,0 +1,20 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;
+
+public sealed class SubmitForReviewCommandHandler : IRequestHandler<SubmitForReviewCommand, Result<Unit>>
+{
+    private readonly IContentPipeline _pipeline;
+
+    public SubmitForReviewCommandHandler(IContentPipeline pipeline)
+    {
+        _pipeline = pipeline;
+    }
+
+    public Task<Result<Unit>> Handle(SubmitForReviewCommand request, CancellationToken cancellationToken)
+    {
+        return _pipeline.SubmitForReviewAsync(request.ContentId, cancellationToken);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommand.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommand.cs
new file mode 100644
index 0000000..0b731eb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommand.cs
@@ -0,0 +1,6 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.ValidateVoice;
+
+public sealed record ValidateVoiceCommand(Guid ContentId) : IRequest<Result<BrandVoiceScore>>;
diff --git a/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommandHandler.cs b/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommandHandler.cs
new file mode 100644
index 0000000..1e7ad9f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Features/Content/Commands/ValidateVoice/ValidateVoiceCommandHandler.cs
@@ -0,0 +1,20 @@
+using MediatR;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Features.Content.Commands.ValidateVoice;
+
+public sealed class ValidateVoiceCommandHandler : IRequestHandler<ValidateVoiceCommand, Result<BrandVoiceScore>>
+{
+    private readonly IContentPipeline _pipeline;
+
+    public ValidateVoiceCommandHandler(IContentPipeline pipeline)
+    {
+        _pipeline = pipeline;
+    }
+
+    public Task<Result<BrandVoiceScore>> Handle(ValidateVoiceCommand request, CancellationToken cancellationToken)
+    {
+        return _pipeline.ValidateVoiceAsync(request.ContentId, cancellationToken);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index a0144fc..db6be9d 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -11,6 +11,7 @@ using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
 using PersonalBrandAssistant.Infrastructure.Data;
 using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
 using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
 using PersonalBrandAssistant.Infrastructure.Services.MediaServices;
 using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
 using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
@@ -74,6 +75,10 @@ public static class DependencyInjection
         services.AddScoped<IContentScheduler, ContentScheduler>();
         services.AddScoped<INotificationService, NotificationService>();
 
+        // Content pipeline
+        services.AddScoped<IBrandVoiceService, StubBrandVoiceService>();
+        services.AddScoped<IContentPipeline, ContentPipeline>();
+
         // Platform integration options
         services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
         services.Configure<MediaStorageOptions>(configuration.GetSection("MediaStorage"));
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs
new file mode 100644
index 0000000..b7b8ae3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs
@@ -0,0 +1,244 @@
+using System.Text;
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+public sealed class ContentPipeline : IContentPipeline
+{
+    private readonly IApplicationDbContext _dbContext;
+    private readonly ISidecarClient _sidecarClient;
+    private readonly IBrandVoiceService _brandVoiceService;
+    private readonly IWorkflowEngine _workflowEngine;
+    private readonly ILogger<ContentPipeline> _logger;
+
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+    };
+
+    public ContentPipeline(
+        IApplicationDbContext dbContext,
+        ISidecarClient sidecarClient,
+        IBrandVoiceService brandVoiceService,
+        IWorkflowEngine workflowEngine,
+        ILogger<ContentPipeline> logger)
+    {
+        _dbContext = dbContext;
+        _sidecarClient = sidecarClient;
+        _brandVoiceService = brandVoiceService;
+        _workflowEngine = workflowEngine;
+        _logger = logger;
+    }
+
+    public async Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct)
+    {
+        if (string.IsNullOrWhiteSpace(request.Topic))
+        {
+            return Result<Guid>.Failure(ErrorCode.ValidationFailed, "Topic is required");
+        }
+
+        var content = Content.Create(
+            request.Type,
+            body: string.Empty,
+            title: null,
+            request.TargetPlatforms);
+
+        content.ParentContentId = request.ParentContentId;
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
+            new { topic = request.Topic, outline = request.Outline }, JsonOptions);
+
+        if (request.Parameters is not null)
+        {
+            foreach (var (key, value) in request.Parameters)
+            {
+                content.Metadata.PlatformSpecificData[key] = value;
+            }
+        }
+
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<Guid>.Success(content.Id);
+    }
+
+    public async Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<string>.NotFound($"Content {contentId} not found");
+        }
+
+        var (topic, _) = ParseGenerationContext(content.Metadata.AiGenerationContext);
+        var prompt = $"Generate a detailed outline for a {content.ContentType} about: {topic}";
+
+        var (text, _, _, _) = await ConsumeEventStreamAsync(prompt, ct);
+        if (text is null)
+        {
+            return Result<string>.Failure(ErrorCode.InternalError, "Sidecar returned no text");
+        }
+
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
+            new { topic, outline = text }, JsonOptions);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<string>.Success(text);
+    }
+
+    public async Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<string>.NotFound($"Content {contentId} not found");
+        }
+
+        var (topic, outline) = ParseGenerationContext(content.Metadata.AiGenerationContext);
+        var brandContext = await LoadBrandContextAsync(ct);
+        var seoKeywords = content.Metadata.SeoKeywords.Count > 0
+            ? string.Join(", ", content.Metadata.SeoKeywords)
+            : null;
+
+        var promptBuilder = new StringBuilder();
+        promptBuilder.AppendLine($"Write a {content.ContentType} about: {topic}");
+        if (outline is not null)
+            promptBuilder.AppendLine($"\nOutline:\n{outline}");
+        if (brandContext is not null)
+            promptBuilder.AppendLine($"\nBrand voice: {brandContext}");
+        if (seoKeywords is not null)
+            promptBuilder.AppendLine($"\nSEO keywords: {seoKeywords}");
+
+        var prompt = promptBuilder.ToString();
+        var (text, filePath, inputTokens, outputTokens) = await ConsumeEventStreamAsync(prompt, ct);
+
+        if (text is null)
+        {
+            return Result<string>.Failure(ErrorCode.InternalError, "Sidecar returned no text");
+        }
+
+        content.Body = text;
+
+        if (filePath is not null)
+        {
+            content.Metadata.PlatformSpecificData["filePath"] = filePath;
+        }
+
+        content.Metadata.TokensUsed = inputTokens + outputTokens;
+        await _dbContext.SaveChangesAsync(ct);
+
+        return Result<string>.Success(text);
+    }
+
+    public async Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<BrandVoiceScore>.NotFound($"Content {contentId} not found");
+        }
+
+        var scoreResult = await _brandVoiceService.ScoreContentAsync(contentId, ct);
+        if (!scoreResult.IsSuccess)
+        {
+            return scoreResult;
+        }
+
+        content.Metadata.PlatformSpecificData["brandVoiceScore"] =
+            JsonSerializer.Serialize(scoreResult.Value, JsonOptions);
+        await _dbContext.SaveChangesAsync(ct);
+
+        return scoreResult;
+    }
+
+    public async Task<Result<MediatR.Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found");
+        }
+
+        var transitionResult = await _workflowEngine.TransitionAsync(
+            contentId, ContentStatus.Review,
+            "Submitted via content pipeline", ActorType.System, ct);
+
+        if (!transitionResult.IsSuccess)
+        {
+            return Result<MediatR.Unit>.Failure(transitionResult.ErrorCode, transitionResult.Errors.ToArray());
+        }
+
+        if (await _workflowEngine.ShouldAutoApproveAsync(contentId, ct))
+        {
+            await _workflowEngine.TransitionAsync(
+                contentId, ContentStatus.Approved,
+                "Auto-approved by autonomy policy", ActorType.System, ct);
+        }
+
+        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
+    }
+
+    private async Task<(string? Text, string? FilePath, int InputTokens, int OutputTokens)>
+        ConsumeEventStreamAsync(string prompt, CancellationToken ct)
+    {
+        var textBuilder = new StringBuilder();
+        string? filePath = null;
+        int inputTokens = 0, outputTokens = 0;
+
+        await foreach (var evt in _sidecarClient.SendTaskAsync(prompt, null, null, ct))
+        {
+            switch (evt)
+            {
+                case ChatEvent { Text: not null } chat:
+                    textBuilder.Append(chat.Text);
+                    break;
+                case FileChangeEvent file:
+                    filePath = file.FilePath;
+                    break;
+                case TaskCompleteEvent complete:
+                    inputTokens = complete.InputTokens;
+                    outputTokens = complete.OutputTokens;
+                    break;
+                case ErrorEvent error:
+                    _logger.LogError("Sidecar error during content pipeline: {Message}", error.Message);
+                    return (null, null, 0, 0);
+            }
+        }
+
+        var text = textBuilder.Length > 0 ? textBuilder.ToString() : null;
+        return (text, filePath, inputTokens, outputTokens);
+    }
+
+    private static (string? Topic, string? Outline) ParseGenerationContext(string? json)
+    {
+        if (string.IsNullOrEmpty(json)) return (null, null);
+
+        try
+        {
+            var doc = JsonSerializer.Deserialize<JsonElement>(json);
+            var topic = doc.TryGetProperty("topic", out var t) ? t.GetString() : null;
+            var outline = doc.TryGetProperty("outline", out var o) && o.ValueKind != JsonValueKind.Null
+                ? o.GetString() : null;
+            return (topic, outline);
+        }
+        catch
+        {
+            return (null, null);
+        }
+    }
+
+    private async Task<string?> LoadBrandContextAsync(CancellationToken ct)
+    {
+        var profile = await _dbContext.BrandProfiles.FirstOrDefaultAsync(p => p.IsActive, ct);
+        if (profile is null) return null;
+
+        return $"{profile.PersonaDescription}. Tone: {string.Join(", ", profile.ToneDescriptors)}. " +
+               $"Style: {profile.StyleGuidelines}";
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
new file mode 100644
index 0000000..0f832d6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
@@ -0,0 +1,17 @@
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+/// <summary>
+/// Placeholder until section-07 (Brand Voice) is implemented.
+/// Returns a perfect score so the pipeline can function end-to-end.
+/// </summary>
+public sealed class StubBrandVoiceService : IBrandVoiceService
+{
+    public Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct)
+    {
+        var score = new BrandVoiceScore(100, 100, 100, 100, [], []);
+        return Task.FromResult(Result<BrandVoiceScore>.Success(score));
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateFromTopicCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateFromTopicCommandHandlerTests.cs
new file mode 100644
index 0000000..00209cf
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateFromTopicCommandHandlerTests.cs
@@ -0,0 +1,47 @@
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Features.Content.Commands.CreateFromTopic;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class CreateFromTopicCommandHandlerTests
+{
+    private readonly Mock<IContentPipeline> _pipeline = new();
+
+    [Fact]
+    public async Task Handle_ValidCommand_DelegatesToContentPipeline()
+    {
+        var contentId = Guid.NewGuid();
+        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<Guid>.Success(contentId));
+
+        var handler = new CreateFromTopicCommandHandler(_pipeline.Object);
+        var command = new CreateFromTopicCommand(ContentType.BlogPost, "AI trends");
+
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(contentId, result.Value);
+        _pipeline.Verify(p => p.CreateFromTopicAsync(
+            It.Is<ContentCreationRequest>(r => r.Topic == "AI trends" && r.Type == ContentType.BlogPost),
+            It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_PipelineReturnsFailure_ReturnsFailure()
+    {
+        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<Guid>.Failure(ErrorCode.ValidationFailed, "Topic is empty"));
+
+        var handler = new CreateFromTopicCommandHandler(_pipeline.Object);
+        var command = new CreateFromTopicCommand(ContentType.BlogPost, "");
+
+        var result = await handler.Handle(command, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateDraftCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateDraftCommandHandlerTests.cs
new file mode 100644
index 0000000..4e70ca2
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateDraftCommandHandlerTests.cs
@@ -0,0 +1,39 @@
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Features.Content.Commands.GenerateDraft;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class GenerateDraftCommandHandlerTests
+{
+    private readonly Mock<IContentPipeline> _pipeline = new();
+
+    [Fact]
+    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
+    {
+        var contentId = Guid.NewGuid();
+        _pipeline.Setup(p => p.GenerateDraftAsync(contentId, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("Draft content here"));
+
+        var handler = new GenerateDraftCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new GenerateDraftCommand(contentId), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.GenerateDraftAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotFound_ReturnsNotFound()
+    {
+        _pipeline.Setup(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.NotFound("Not found"));
+
+        var handler = new GenerateDraftCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new GenerateDraftCommand(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateOutlineCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateOutlineCommandHandlerTests.cs
new file mode 100644
index 0000000..5d23827
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/GenerateOutlineCommandHandlerTests.cs
@@ -0,0 +1,39 @@
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Features.Content.Commands.GenerateOutline;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class GenerateOutlineCommandHandlerTests
+{
+    private readonly Mock<IContentPipeline> _pipeline = new();
+
+    [Fact]
+    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
+    {
+        var contentId = Guid.NewGuid();
+        _pipeline.Setup(p => p.GenerateOutlineAsync(contentId, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.Success("1. Intro\n2. Body"));
+
+        var handler = new GenerateOutlineCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new GenerateOutlineCommand(contentId), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.GenerateOutlineAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_ContentNotFound_ReturnsNotFound()
+    {
+        _pipeline.Setup(p => p.GenerateOutlineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<string>.NotFound("Not found"));
+
+        var handler = new GenerateOutlineCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new GenerateOutlineCommand(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/SubmitForReviewCommandHandlerTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/SubmitForReviewCommandHandlerTests.cs
new file mode 100644
index 0000000..d9ab679
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/SubmitForReviewCommandHandlerTests.cs
@@ -0,0 +1,39 @@
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Features.Content.Commands.SubmitForReview;
+
+namespace PersonalBrandAssistant.Application.Tests.Features.Content.Commands;
+
+public class SubmitForReviewCommandHandlerTests
+{
+    private readonly Mock<IContentPipeline> _pipeline = new();
+
+    [Fact]
+    public async Task Handle_ValidContentId_DelegatesToContentPipeline()
+    {
+        var contentId = Guid.NewGuid();
+        _pipeline.Setup(p => p.SubmitForReviewAsync(contentId, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+
+        var handler = new SubmitForReviewCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new SubmitForReviewCommand(contentId), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _pipeline.Verify(p => p.SubmitForReviewAsync(contentId, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_ContentAlreadySubmitted_ReturnsConflict()
+    {
+        _pipeline.Setup(p => p.SubmitForReviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Conflict("Already in review"));
+
+        var handler = new SubmitForReviewCommandHandler(_pipeline.Object);
+        var result = await handler.Handle(new SubmitForReviewCommand(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs
new file mode 100644
index 0000000..75c6fb4
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs
@@ -0,0 +1,380 @@
+using System.Runtime.CompilerServices;
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class ContentPipelineTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<ISidecarClient> _sidecarClient = new();
+    private readonly Mock<IBrandVoiceService> _brandVoiceService = new();
+    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
+    private readonly Mock<ILogger<ContentPipeline>> _logger = new();
+
+    private ContentPipeline CreatePipeline() =>
+        new(_dbContext.Object, _sidecarClient.Object, _brandVoiceService.Object,
+            _workflowEngine.Object, _logger.Object);
+
+    private void SetupContentsDbSet(List<Content> contents)
+    {
+        var mockDbSet = contents.AsQueryable().BuildMockDbSet();
+        mockDbSet.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .Returns<object[], CancellationToken>((keys, _) =>
+                new ValueTask<Content?>(contents.FirstOrDefault(c => c.Id == (Guid)keys[0])));
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
+    }
+
+    private void SetupBrandProfiles()
+    {
+        var profile = new BrandProfile
+        {
+            Name = "Test Brand",
+            PersonaDescription = "Test persona",
+            StyleGuidelines = "Be concise",
+            IsActive = true,
+        };
+        profile.ToneDescriptors = ["professional"];
+        profile.Topics = ["tech"];
+        profile.ExampleContent = [];
+        profile.VocabularyPreferences = new VocabularyConfig
+        {
+            PreferredTerms = ["AI"],
+            AvoidTerms = ["synergy"],
+        };
+
+        var dbSet = new List<BrandProfile> { profile }.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(x => x.BrandProfiles).Returns(dbSet.Object);
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("session-1", 100, 50);
+        await Task.CompletedTask;
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEventsWithFile(
+        string text, string filePath, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new FileChangeEvent(filePath, "created");
+        yield return new TaskCompleteEvent("session-1", 200, 100);
+        await Task.CompletedTask;
+    }
+
+    private void SetupSidecarResponse(string text)
+    {
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
+                It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    // --- CreateFromTopicAsync ---
+
+    [Fact]
+    public async Task CreateFromTopicAsync_ValidRequest_CreatesContentInDraftStatus()
+    {
+        Content? captured = null;
+        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
+        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => captured = c);
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
+
+        var pipeline = CreatePipeline();
+        var request = new ContentCreationRequest(
+            ContentType.BlogPost, "AI in branding", null, null, null, null);
+
+        var result = await pipeline.CreateFromTopicAsync(request, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotNull(captured);
+        Assert.Equal(ContentStatus.Draft, captured!.Status);
+        Assert.Contains("AI in branding", captured.Metadata.AiGenerationContext!);
+    }
+
+    [Fact]
+    public async Task CreateFromTopicAsync_EmptyTopic_ReturnsValidationFailure()
+    {
+        SetupContentsDbSet([]);
+        var pipeline = CreatePipeline();
+        var request = new ContentCreationRequest(
+            ContentType.BlogPost, "", null, null, null, null);
+
+        var result = await pipeline.CreateFromTopicAsync(request, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task CreateFromTopicAsync_WithParentContentId_SetsParentOnContent()
+    {
+        Content? captured = null;
+        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
+        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => captured = c);
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
+
+        var parentId = Guid.NewGuid();
+        var pipeline = CreatePipeline();
+        var request = new ContentCreationRequest(
+            ContentType.SocialPost, "Test topic", null, null, parentId, null);
+
+        await pipeline.CreateFromTopicAsync(request, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal(parentId, captured!.ParentContentId);
+    }
+
+    [Fact]
+    public async Task CreateFromTopicAsync_WithTargetPlatforms_SetsTargetPlatformsOnContent()
+    {
+        Content? captured = null;
+        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
+        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
+            .Callback<Content>(c => captured = c);
+        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
+
+        var pipeline = CreatePipeline();
+        var request = new ContentCreationRequest(
+            ContentType.SocialPost, "Test topic", null,
+            [PlatformType.TwitterX, PlatformType.LinkedIn], null, null);
+
+        await pipeline.CreateFromTopicAsync(request, CancellationToken.None);
+
+        Assert.NotNull(captured);
+        Assert.Equal([PlatformType.TwitterX, PlatformType.LinkedIn], captured!.TargetPlatforms);
+    }
+
+    // --- GenerateOutlineAsync ---
+
+    [Fact]
+    public async Task GenerateOutlineAsync_ValidContentId_SendsOutlineTaskToSidecar()
+    {
+        var content = Content.Create(ContentType.BlogPost, string.Empty);
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI trends" });
+        SetupContentsDbSet([content]);
+        SetupSidecarResponse("## Outline\n1. Intro\n2. Body\n3. Conclusion");
+
+        var pipeline = CreatePipeline();
+        await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);
+
+        _sidecarClient.Verify(c => c.SendTaskAsync(
+            It.Is<string>(s => s.Contains("AI trends") && s.Contains("outline", StringComparison.OrdinalIgnoreCase)),
+            It.IsAny<string?>(),
+            It.IsAny<string?>(),
+            It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task GenerateOutlineAsync_ValidContentId_StoresOutlineInMetadata()
+    {
+        var content = Content.Create(ContentType.BlogPost, string.Empty);
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI trends" });
+        SetupContentsDbSet([content]);
+        SetupSidecarResponse("## Outline\n1. Intro\n2. Body");
+
+        var pipeline = CreatePipeline();
+        await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);
+
+        var ctx = JsonSerializer.Deserialize<JsonElement>(content.Metadata.AiGenerationContext!);
+        Assert.True(ctx.TryGetProperty("outline", out var outline));
+        Assert.Contains("Outline", outline.GetString());
+    }
+
+    [Fact]
+    public async Task GenerateOutlineAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet([]);
+        var pipeline = CreatePipeline();
+
+        var result = await pipeline.GenerateOutlineAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task GenerateOutlineAsync_ReturnsOutlineText()
+    {
+        var content = Content.Create(ContentType.BlogPost, string.Empty);
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI" });
+        SetupContentsDbSet([content]);
+        SetupSidecarResponse("1. Intro\n2. Body\n3. Outro");
+
+        var pipeline = CreatePipeline();
+        var result = await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("1. Intro\n2. Body\n3. Outro", result.Value);
+    }
+
+    // --- GenerateDraftAsync ---
+
+    [Fact]
+    public async Task GenerateDraftAsync_SocialPost_UpdatesContentBody()
+    {
+        var content = Content.Create(ContentType.SocialPost, string.Empty);
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
+            new { topic = "AI tips", outline = "1. Tip one 2. Tip two" });
+        SetupContentsDbSet([content]);
+        SetupBrandProfiles();
+        SetupSidecarResponse("Here are 5 AI tips for your brand...");
+
+        var pipeline = CreatePipeline();
+        await pipeline.GenerateDraftAsync(content.Id, CancellationToken.None);
+
+        Assert.Equal("Here are 5 AI tips for your brand...", content.Body);
+    }
+
+    [Fact]
+    public async Task GenerateDraftAsync_BlogPost_CapturesFilePath()
+    {
+        var content = Content.Create(ContentType.BlogPost, string.Empty);
+        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
+            new { topic = "AI blog", outline = "1. Intro" });
+        SetupContentsDbSet([content]);
+        SetupBrandProfiles();
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
+                It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Returns(CreateSidecarEventsWithFile("<html>Blog content</html>", "/blog/ai-post.html"));
+
+        var pipeline = CreatePipeline();
+        await pipeline.GenerateDraftAsync(content.Id, CancellationToken.None);
+
+        Assert.Equal("/blog/ai-post.html", content.Metadata.PlatformSpecificData["filePath"]);
+    }
+
+    [Fact]
+    public async Task GenerateDraftAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet([]);
+        var pipeline = CreatePipeline();
+
+        var result = await pipeline.GenerateDraftAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- ValidateVoiceAsync ---
+
+    [Fact]
+    public async Task ValidateVoiceAsync_DelegatesToBrandVoiceService()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Some content");
+        SetupContentsDbSet([content]);
+        var score = new BrandVoiceScore(85, 90, 80, 85, [], []);
+        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<BrandVoiceScore>.Success(score));
+
+        var pipeline = CreatePipeline();
+        await pipeline.ValidateVoiceAsync(content.Id, CancellationToken.None);
+
+        _brandVoiceService.Verify(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ValidateVoiceAsync_ReturnsBrandVoiceScore()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Some content");
+        SetupContentsDbSet([content]);
+        var score = new BrandVoiceScore(85, 90, 80, 85, [], []);
+        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<BrandVoiceScore>.Success(score));
+
+        var pipeline = CreatePipeline();
+        var result = await pipeline.ValidateVoiceAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(85, result.Value!.OverallScore);
+    }
+
+    [Fact]
+    public async Task ValidateVoiceAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet([]);
+        var pipeline = CreatePipeline();
+
+        var result = await pipeline.ValidateVoiceAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    // --- SubmitForReviewAsync ---
+
+    [Fact]
+    public async Task SubmitForReviewAsync_TransitionsContentToReview()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Content body");
+        SetupContentsDbSet([content]);
+        _workflowEngine.Setup(w => w.TransitionAsync(
+                content.Id, ContentStatus.Review, It.IsAny<string?>(),
+                It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+        _workflowEngine.Setup(w => w.ShouldAutoApproveAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(false);
+
+        var pipeline = CreatePipeline();
+        var result = await pipeline.SubmitForReviewAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _workflowEngine.Verify(w => w.TransitionAsync(
+            content.Id, ContentStatus.Review, It.IsAny<string?>(),
+            It.IsAny<ActorType>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task SubmitForReviewAsync_AutonomousLevel_AutoApproves()
+    {
+        var content = Content.Create(ContentType.BlogPost, "Content body");
+        SetupContentsDbSet([content]);
+        _workflowEngine.Setup(w => w.TransitionAsync(
+                content.Id, It.IsAny<ContentStatus>(), It.IsAny<string?>(),
+                It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+        _workflowEngine.Setup(w => w.ShouldAutoApproveAsync(content.Id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(true);
+
+        var pipeline = CreatePipeline();
+        await pipeline.SubmitForReviewAsync(content.Id, CancellationToken.None);
+
+        _workflowEngine.Verify(w => w.TransitionAsync(
+            content.Id, ContentStatus.Approved, It.IsAny<string?>(),
+            It.IsAny<ActorType>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task SubmitForReviewAsync_ContentNotFound_ReturnsNotFound()
+    {
+        SetupContentsDbSet([]);
+        var pipeline = CreatePipeline();
+
+        var result = await pipeline.SubmitForReviewAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+}
