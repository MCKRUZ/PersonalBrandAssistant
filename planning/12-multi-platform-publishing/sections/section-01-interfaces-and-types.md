# Section 01: Interfaces and Types

## Overview

This section adds the `Medium` value to the `Platform` enum and defines all shared interfaces and record types that the multi-platform publishing system depends on. Everything lives in the Application and Domain layers. No implementation logic, no tests for type definitions -- this is pure contract definition.

This section has **no dependencies** and **blocks** sections 03, 04, 05, and 07-10.

---

## Tests

The TDD plan explicitly states: **"No tests needed -- these are interfaces and records (type definitions only)."**

There are no test stubs for this section. The types defined here are validated implicitly by the tests in every downstream section that depends on them.

---

## Implementation

### Step 1: Add `Medium` to `Platform` Enum

**File:** `src/PBA.Domain/Enums/Platform.cs`

The current enum has: `Blog`, `Substack`, `LinkedIn`, `Twitter`, `Reddit`, `YouTube`. Add `Medium` as a new value. Insert it after `Blog` to keep the enum logically grouped (publishing platforms first).

```csharp
public enum Platform
{
    Blog,
    Medium,
    Substack,
    LinkedIn,
    Twitter,
    Reddit,
    YouTube
}
```

**Risk:** Adding a new enum value mid-list changes the integer values of everything after it. Check if any existing code persists `Platform` as an integer (EF column, JSON serialization). If the database stores `Platform` as an integer column, existing rows for Substack/LinkedIn/Twitter/Reddit/YouTube will all shift by +1. To avoid this, either:
- Add `Medium` at the end of the enum (safest), or
- Assign explicit integer values to all members

The safer choice is explicit integer values:

```csharp
public enum Platform
{
    Blog = 0,
    Substack = 1,
    LinkedIn = 2,
    Twitter = 3,
    Reddit = 4,
    YouTube = 5,
    Medium = 6
}
```

Check the EF configuration for `Platform` to confirm whether it is stored as a string or integer. If stored as a string (`.HasConversion<string>()`), insertion order does not matter and `Medium` can go anywhere in the enum.

### Step 2: Add `PublishMode` Enum

**File:** `src/PBA.Domain/Enums/PublishMode.cs` (new file)

Used by `PlatformPublishRequest` to indicate whether the connector should create a draft, publish immediately, or schedule.

```csharp
namespace PBA.Domain.Enums;

public enum PublishMode
{
    Draft,
    Publish,
    Schedule
}
```

### Step 3: Define `IPlatformConnector` Interface

**File:** `src/PBA.Application/Common/Interfaces/IPlatformConnector.cs` (new file)

This replaces the role of `IBlogConnector` as the unified contract for all platform connectors. Every platform (Blog, Medium, Substack, LinkedIn, Twitter) implements this interface and is registered via keyed DI using `Platform` enum values.

```csharp
namespace PBA.Application.Common.Interfaces;

using PBA.Application.Common.Models;
using PBA.Domain.Enums;

public interface IPlatformConnector
{
    Platform Platform { get; }
    Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct);
    PlatformCapabilities GetCapabilities();
}
```

### Step 4: Define `IPlatformFormatter` Interface

**File:** `src/PBA.Application/Common/Interfaces/IPlatformFormatter.cs` (new file)

Per-platform content formatters. Registered via keyed DI identical to the connector pattern. Called by `IContentTransformer` after shared preprocessing.

```csharp
namespace PBA.Application.Common.Interfaces;

using PBA.Application.Common.Models;
using PBA.Domain.Enums;

public interface IPlatformFormatter
{
    Platform Platform { get; }
    Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct);
}
```

### Step 5: Define `IContentTransformer` Interface

**File:** `src/PBA.Application/Common/Interfaces/IContentTransformer.cs` (new file)

Orchestrates the transformation pipeline: shared preprocessing followed by platform-specific formatting.

```csharp
namespace PBA.Application.Common.Interfaces;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

public interface IContentTransformer
{
    Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct);
}
```

### Step 6: Define Supporting Record Types

**File:** `src/PBA.Application/Common/Models/PlatformPublishRequest.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

public record PlatformPublishRequest(
    Content Content,
    string TransformedContent,
    IReadOnlyList<string> Tags,
    string? CanonicalUrl,
    PublishMode Mode
);
```

**File:** `src/PBA.Application/Common/Models/PlatformPublishResult.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

public record PlatformPublishResult(
    bool Success,
    string? PublishedUrl,
    string? PlatformPostId,
    string? ErrorMessage
);
```

**File:** `src/PBA.Application/Common/Models/PlatformCapabilities.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes
);
```

**File:** `src/PBA.Application/Common/Models/PreprocessedContent.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

public record PreprocessedContent(
    string Title,
    string Body,
    string? CanonicalUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ImageReference> Images
);
```

**File:** `src/PBA.Application/Common/Models/ImageReference.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

public record ImageReference(
    string OriginalPath,
    string AbsoluteUrl,
    string? AltText
);
```

**File:** `src/PBA.Application/Common/Models/PublishResult.cs` (new file)

```csharp
namespace PBA.Application.Common.Models;

using PBA.Domain.Enums;

public record PublishResult(
    bool PrimarySuccess,
    string? PrimaryUrl,
    IReadOnlyList<PlatformPublishOutcome> SecondaryOutcomes
);

public record PlatformPublishOutcome(
    Platform Platform,
    bool Success,
    string? Url,
    string? Error
);
```

---

## File Summary

All files created/modified by this section:

| File | Layer | Description |
|------|-------|-------------|
| `src/PBA.Domain/Enums/Platform.cs` | Domain | Modified -- add `Medium = 6`, explicit int values for all members |
| `src/PBA.Domain/Enums/PublishMode.cs` | Domain | New -- `Draft = 0`, `Publish = 1`, `Schedule = 2` (explicit ints) |
| `src/PBA.Application/Common/Interfaces/IPlatformConnector.cs` | Application | New -- unified connector interface |
| `src/PBA.Application/Common/Interfaces/IPlatformFormatter.cs` | Application | New -- per-platform formatter interface |
| `src/PBA.Application/Common/Interfaces/IContentTransformer.cs` | Application | New -- transformation pipeline interface |
| `src/PBA.Application/Common/Models/PlatformPublishRequest.cs` | Application | New -- request record for connectors (includes ScheduledAt) |
| `src/PBA.Application/Common/Models/PlatformPublishResult.cs` | Application | New -- result record from connectors |
| `src/PBA.Application/Common/Models/PlatformCapabilities.cs` | Application | New -- platform capability descriptor |
| `src/PBA.Application/Common/Models/PreprocessedContent.cs` | Application | New -- preprocessed content for formatters |
| `src/PBA.Application/Common/Models/ImageReference.cs` | Application | New -- image reference from preprocessing |
| `src/PBA.Application/Common/Models/PublishResult.cs` | Application | New -- aggregate multi-platform publish result |
| `src/PBA.Application/Common/Models/PlatformPublishOutcome.cs` | Application | New -- per-platform outcome record (split from PublishResult.cs) |

## Deviations from Plan

1. **Explicit integer values on both enums** -- Platform stored as int in EF Core; added explicit values to both Platform and PublishMode to prevent silent data corruption if enum members are reordered.
2. **PlatformPublishOutcome split to own file** -- Plan had it in PublishResult.cs; split per one-class-per-file convention.
3. **ScheduledAt added to PlatformPublishRequest** -- Plan omitted scheduling timestamp; added `DateTimeOffset? ScheduledAt` so connectors have a clean contract without digging into Content entity.

---

## Dependencies on Other Sections

This section has **no dependencies**. It can be implemented first, in parallel with section-02 (domain model changes).

**Downstream consumers:**
- **Section 03** (Content Transformation) implements `IContentTransformer` and `IPlatformFormatter`, uses `PreprocessedContent` and `ImageReference`
- **Section 04** (Blog Connector Migration) adapts `BlogConnector` to implement `IPlatformConnector`, uses `PlatformPublishRequest` and `PlatformPublishResult`
- **Section 05** (Publisher Refactor) uses `PublishResult`, `PlatformPublishOutcome`, `PlatformPublishRequest`, `PlatformPublishResult`
- **Sections 07-10** (Platform Connectors) implement `IPlatformConnector` and `IPlatformFormatter`, use all model types

---

## Verification

After implementation, confirm:
1. `dotnet build` succeeds for `PBA.Domain` and `PBA.Application` projects -- **VERIFIED**: all 4 src projects build clean
2. `dotnet test` passes -- **VERIFIED**: 427 tests pass (pre-existing test errors in ContentType.BlogPost and Idea.SourceName are unrelated)
3. The `Medium` enum value does not shift existing integer values -- **VERIFIED**: explicit int values assigned, Medium = 6 appended at end
