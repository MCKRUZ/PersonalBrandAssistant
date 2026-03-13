namespace PersonalBrandAssistant.Domain.Common;

public abstract class EntityBase
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AuditableEntityBase : EntityBase, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
