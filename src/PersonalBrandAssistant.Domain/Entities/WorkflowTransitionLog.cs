using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class WorkflowTransitionLog : EntityBase
{
    private WorkflowTransitionLog() { }

    public Guid ContentId { get; private init; }
    public ContentStatus FromStatus { get; private init; }
    public ContentStatus ToStatus { get; private init; }
    public string? Reason { get; private init; }
    public ActorType ActorType { get; private init; }
    public string? ActorId { get; private init; }
    public DateTimeOffset Timestamp { get; private init; }

    public static WorkflowTransitionLog Create(
        Guid contentId,
        ContentStatus from,
        ContentStatus to,
        ActorType actorType,
        string? actorId = null,
        string? reason = null) =>
        new()
        {
            ContentId = contentId,
            FromStatus = from,
            ToStatus = to,
            ActorType = actorType,
            ActorId = actorId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow,
        };
}
