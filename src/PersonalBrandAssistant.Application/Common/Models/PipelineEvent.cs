namespace PersonalBrandAssistant.Application.Common.Models;

public sealed record PipelineEvent(
    string EventType,
    string Data);
