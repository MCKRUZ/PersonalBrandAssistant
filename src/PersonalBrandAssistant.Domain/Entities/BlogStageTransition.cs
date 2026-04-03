using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public sealed record BlogStageTransition(
    BlogPipelineStage FromStage,
    BlogPipelineStage ToStage,
    DateTimeOffset TransitionedAt,
    string? Note = null);
