# Section 08: Notification System

## Overview

Implements `INotificationService` for blog workflow human-in-the-loop points: Substack publication detected (prompt to schedule blog) and blog deploy date reached (prompt to trigger publish). Uses `UserNotification` entity with idempotent creation via unique filtered index.

**Depends on:** Section 01 (UserNotification entity, NotificationStatus enum)
**Blocks:** Section 07, 12, 13

---

## Tests (Write First)

### NotificationService Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/NotificationServiceTests.cs`

```csharp
// Test: CreateNotificationAsync stores notification with Pending status
// Test: CreateNotificationAsync is idempotent (unique constraint on ContentId + Type for Pending)
// Test: CreateNotificationAsync returns existing notification if duplicate
// Test: AcknowledgeAsync sets status to Acknowledged with timestamp
// Test: ActAsync sets status to Acted
// Test: GetPendingAsync returns only Pending status notifications
// Test: GetPendingAsync filters by content ID when specified
```

### Endpoint Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/NotificationEndpointsTests.cs`

```csharp
// Test: GET /api/notifications returns pending notifications
// Test: POST /api/notifications/{id}/acknowledge updates status
// Test: POST /api/notifications/{id}/act triggers appropriate action
// Test: POST /api/notifications/{id}/act returns 404 for non-existent
```

---

## Implementation Details

### Interface
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/INotificationService.cs`

```csharp
public interface INotificationService
{
    Task<UserNotification> CreateNotificationAsync(string type, string message, Guid? contentId, CancellationToken ct);
    Task<IReadOnlyList<UserNotification>> GetPendingAsync(Guid? contentId, CancellationToken ct);
    Task AcknowledgeAsync(Guid notificationId, CancellationToken ct);
    Task ActAsync(Guid notificationId, CancellationToken ct);
}
```

### Service
File: `src/PersonalBrandAssistant.Infrastructure/Services/NotificationService.cs`

**CreateNotificationAsync**: Check for existing pending notification with same (ContentId, Type). If found, return it (idempotent). Otherwise create new with Status=Pending. The unique filtered index on `(ContentId, Type) WHERE Status = Pending` enforces this at DB level as safety net.

**GetPendingAsync**: Query `UserNotifications.Where(n => n.Status == Pending)`, optionally filtered by contentId.

**AcknowledgeAsync**: Load notification, set Status=Acknowledged, AcknowledgedAt=UtcNow.

**ActAsync**: Load notification, set Status=Acted. The caller determines what action to take (e.g., BlogSchedulingService.ConfirmBlogScheduleAsync).

### Endpoints
File: `src/PersonalBrandAssistant.Api/Endpoints/NotificationEndpoints.cs`

```
GET  /api/notifications                  → List pending (optional ?contentId= filter)
POST /api/notifications/{id}/acknowledge → Mark acknowledged
POST /api/notifications/{id}/act         → Mark acted (caller handles action)
```

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Interfaces/INotificationService.cs` | Create |
| `Infrastructure/Services/NotificationService.cs` | Create |
| `Api/Endpoints/NotificationEndpoints.cs` | Create |
| `Infrastructure/DependencyInjection.cs` | Modify |
| `Api/Program.cs` | Modify |
