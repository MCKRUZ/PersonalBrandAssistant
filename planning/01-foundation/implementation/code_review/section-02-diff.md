diff --git a/src/PersonalBrandAssistant.Domain/Common/EntityBase.cs b/src/PersonalBrandAssistant.Domain/Common/EntityBase.cs
new file mode 100644
index 0000000..3dae5e0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Common/EntityBase.cs
@@ -0,0 +1,16 @@
+namespace PersonalBrandAssistant.Domain.Common;
+
+public abstract class EntityBase : IAuditable
+{
+    private readonly List<IDomainEvent> _domainEvents = [];
+
+    public Guid Id { get; protected init; } = Guid.CreateVersion7();
+    public DateTimeOffset CreatedAt { get; set; }
+    public DateTimeOffset UpdatedAt { get; set; }
+
+    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
+
+    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
+
+    public void ClearDomainEvents() => _domainEvents.Clear();
+}
diff --git a/src/PersonalBrandAssistant.Domain/Common/IAuditable.cs b/src/PersonalBrandAssistant.Domain/Common/IAuditable.cs
new file mode 100644
index 0000000..0d11026
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Common/IAuditable.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Domain.Common;
+
+public interface IAuditable
+{
+    DateTimeOffset CreatedAt { get; set; }
+    DateTimeOffset UpdatedAt { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Common/IDomainEvent.cs b/src/PersonalBrandAssistant.Domain/Common/IDomainEvent.cs
new file mode 100644
index 0000000..1dc7463
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Common/IDomainEvent.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Common;
+
+public interface IDomainEvent;
diff --git a/src/PersonalBrandAssistant.Domain/Entities/AuditLogEntry.cs b/src/PersonalBrandAssistant.Domain/Entities/AuditLogEntry.cs
new file mode 100644
index 0000000..442793f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/AuditLogEntry.cs
@@ -0,0 +1,14 @@
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class AuditLogEntry : EntityBase
+{
+    public string EntityType { get; set; } = string.Empty;
+    public Guid EntityId { get; set; }
+    public string Action { get; set; } = string.Empty;
+    public string? OldValue { get; set; }
+    public string? NewValue { get; set; }
+    public DateTimeOffset Timestamp { get; set; }
+    public string? Details { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/BrandProfile.cs b/src/PersonalBrandAssistant.Domain/Entities/BrandProfile.cs
new file mode 100644
index 0000000..3acc20a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/BrandProfile.cs
@@ -0,0 +1,17 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class BrandProfile : EntityBase
+{
+    public string Name { get; set; } = string.Empty;
+    public List<string> ToneDescriptors { get; set; } = [];
+    public string StyleGuidelines { get; set; } = string.Empty;
+    public VocabularyConfig VocabularyPreferences { get; set; } = new();
+    public List<string> Topics { get; set; } = [];
+    public string PersonaDescription { get; set; } = string.Empty;
+    public List<string> ExampleContent { get; set; } = [];
+    public bool IsActive { get; set; }
+    public uint Version { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/Content.cs b/src/PersonalBrandAssistant.Domain/Entities/Content.cs
new file mode 100644
index 0000000..4a4a423
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/Content.cs
@@ -0,0 +1,63 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.Events;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class Content : EntityBase
+{
+    private static readonly Dictionary<ContentStatus, ContentStatus[]> AllowedTransitions = new()
+    {
+        [ContentStatus.Draft] = [ContentStatus.Review, ContentStatus.Archived],
+        [ContentStatus.Review] = [ContentStatus.Draft, ContentStatus.Approved, ContentStatus.Archived],
+        [ContentStatus.Approved] = [ContentStatus.Scheduled, ContentStatus.Draft, ContentStatus.Archived],
+        [ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Draft, ContentStatus.Archived],
+        [ContentStatus.Publishing] = [ContentStatus.Published, ContentStatus.Failed],
+        [ContentStatus.Published] = [ContentStatus.Archived],
+        [ContentStatus.Failed] = [ContentStatus.Draft, ContentStatus.Archived],
+        [ContentStatus.Archived] = [ContentStatus.Draft],
+    };
+
+    private Content() { }
+
+    public ContentType ContentType { get; private init; }
+    public string? Title { get; set; }
+    public string Body { get; set; } = string.Empty;
+    public ContentStatus Status { get; private set; } = ContentStatus.Draft;
+    public ContentMetadata Metadata { get; set; } = new();
+    public Guid? ParentContentId { get; set; }
+    public PlatformType[] TargetPlatforms { get; set; } = [];
+    public DateTimeOffset? ScheduledAt { get; set; }
+    public DateTimeOffset? PublishedAt { get; set; }
+    public uint Version { get; set; }
+
+    public static Content Create(
+        ContentType type,
+        string body,
+        string? title = null,
+        PlatformType[]? targetPlatforms = null)
+    {
+        return new Content
+        {
+            ContentType = type,
+            Body = body,
+            Title = title,
+            TargetPlatforms = targetPlatforms ?? [],
+        };
+    }
+
+    public void TransitionTo(ContentStatus newStatus)
+    {
+        if (!AllowedTransitions.TryGetValue(Status, out var allowed) ||
+            !allowed.Contains(newStatus))
+        {
+            throw new InvalidOperationException(
+                $"Cannot transition from {Status} to {newStatus}.");
+        }
+
+        var oldStatus = Status;
+        Status = newStatus;
+        AddDomainEvent(new ContentStateChangedEvent(Id, oldStatus, newStatus));
+    }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs b/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs
new file mode 100644
index 0000000..6b8f4cf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs
@@ -0,0 +1,17 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class ContentCalendarSlot : EntityBase
+{
+    public DateOnly ScheduledDate { get; set; }
+    public TimeOnly? ScheduledTime { get; set; }
+    public string TimeZoneId { get; set; } = string.Empty;
+    public string? Theme { get; set; }
+    public ContentType ContentType { get; set; }
+    public PlatformType TargetPlatform { get; set; }
+    public Guid? ContentId { get; set; }
+    public bool IsRecurring { get; set; }
+    public string? RecurrencePattern { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/Platform.cs b/src/PersonalBrandAssistant.Domain/Entities/Platform.cs
new file mode 100644
index 0000000..942b11d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/Platform.cs
@@ -0,0 +1,19 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class Platform : EntityBase
+{
+    public PlatformType Type { get; set; }
+    public string DisplayName { get; set; } = string.Empty;
+    public bool IsConnected { get; set; }
+    public byte[]? EncryptedAccessToken { get; set; }
+    public byte[]? EncryptedRefreshToken { get; set; }
+    public DateTimeOffset? TokenExpiresAt { get; set; }
+    public PlatformRateLimitState RateLimitState { get; set; } = new();
+    public DateTimeOffset? LastSyncAt { get; set; }
+    public PlatformSettings Settings { get; set; } = new();
+    public uint Version { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/User.cs b/src/PersonalBrandAssistant.Domain/Entities/User.cs
new file mode 100644
index 0000000..f0a3f0d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/User.cs
@@ -0,0 +1,12 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class User : EntityBase
+{
+    public string Email { get; set; } = string.Empty;
+    public string DisplayName { get; set; } = string.Empty;
+    public string TimeZoneId { get; set; } = string.Empty;
+    public UserSettings Settings { get; set; } = new();
+}
diff --git a/src/PersonalBrandAssistant.Domain/Enums/AutonomyLevel.cs b/src/PersonalBrandAssistant.Domain/Enums/AutonomyLevel.cs
new file mode 100644
index 0000000..d1e0323
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/AutonomyLevel.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum AutonomyLevel { Manual, Assisted, SemiAuto, Autonomous }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/ContentStatus.cs b/src/PersonalBrandAssistant.Domain/Enums/ContentStatus.cs
new file mode 100644
index 0000000..14a6000
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/ContentStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum ContentStatus { Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/ContentType.cs b/src/PersonalBrandAssistant.Domain/Enums/ContentType.cs
new file mode 100644
index 0000000..59dda24
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/ContentType.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum ContentType { BlogPost, SocialPost, Thread, VideoDescription }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs b/src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs
new file mode 100644
index 0000000..22f9ca7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum PlatformType { TwitterX, LinkedIn, Instagram, YouTube }
diff --git a/src/PersonalBrandAssistant.Domain/Events/ContentStateChangedEvent.cs b/src/PersonalBrandAssistant.Domain/Events/ContentStateChangedEvent.cs
new file mode 100644
index 0000000..fd9e719
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Events/ContentStateChangedEvent.cs
@@ -0,0 +1,9 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Events;
+
+public sealed record ContentStateChangedEvent(
+    Guid ContentId,
+    ContentStatus OldStatus,
+    ContentStatus NewStatus) : IDomainEvent;
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs
new file mode 100644
index 0000000..4858d32
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs
@@ -0,0 +1,11 @@
+namespace PersonalBrandAssistant.Domain.ValueObjects;
+
+public class ContentMetadata
+{
+    public List<string> Tags { get; set; } = [];
+    public List<string> SeoKeywords { get; set; } = [];
+    public Dictionary<string, string> PlatformSpecificData { get; set; } = new();
+    public string? AiGenerationContext { get; set; }
+    public int? TokensUsed { get; set; }
+    public decimal? EstimatedCost { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs
new file mode 100644
index 0000000..193146a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Domain.ValueObjects;
+
+public class PlatformRateLimitState
+{
+    public int? RemainingCalls { get; set; }
+    public DateTimeOffset? ResetAt { get; set; }
+    public TimeSpan? WindowDuration { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformSettings.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformSettings.cs
new file mode 100644
index 0000000..ce8e226
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/PlatformSettings.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Domain.ValueObjects;
+
+public class PlatformSettings
+{
+    public List<string> DefaultHashtags { get; set; } = [];
+    public int? MaxPostLength { get; set; }
+    public bool AutoCrossPost { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/UserSettings.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/UserSettings.cs
new file mode 100644
index 0000000..67eb837
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/UserSettings.cs
@@ -0,0 +1,10 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.ValueObjects;
+
+public class UserSettings
+{
+    public AutonomyLevel DefaultAutonomyLevel { get; set; } = AutonomyLevel.Manual;
+    public bool NotificationsEnabled { get; set; } = true;
+    public string Theme { get; set; } = "light";
+}
diff --git a/src/PersonalBrandAssistant.Domain/ValueObjects/VocabularyConfig.cs b/src/PersonalBrandAssistant.Domain/ValueObjects/VocabularyConfig.cs
new file mode 100644
index 0000000..1aacd8f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/ValueObjects/VocabularyConfig.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Domain.ValueObjects;
+
+public class VocabularyConfig
+{
+    public List<string> PreferredTerms { get; set; } = [];
+    public List<string> AvoidTerms { get; set; } = [];
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Common/EntityBaseTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Common/EntityBaseTests.cs
new file mode 100644
index 0000000..990210a
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Common/EntityBaseTests.cs
@@ -0,0 +1,28 @@
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Domain.Tests.Common;
+
+public class EntityBaseTests
+{
+    private class TestEntity : EntityBase;
+
+    [Fact]
+    public void Id_IsValidUuidV7()
+    {
+        var entity = new TestEntity();
+        var bytes = entity.Id.ToByteArray();
+        // UUIDv7: version nibble at byte[7] high nibble should be 0x70
+        var version = (bytes[7] >> 4) & 0x0F;
+        Assert.Equal(7, version);
+    }
+
+    [Fact]
+    public void SequentialEntities_HaveChronologicallyOrderedIds()
+    {
+        var first = new TestEntity();
+        var second = new TestEntity();
+        // UUIDv7 string representation sorts chronologically
+        Assert.True(
+            string.Compare(second.Id.ToString(), first.Id.ToString(), StringComparison.Ordinal) >= 0);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/AuditLogEntryTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AuditLogEntryTests.cs
new file mode 100644
index 0000000..f79272a
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AuditLogEntryTests.cs
@@ -0,0 +1,39 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class AuditLogEntryTests
+{
+    [Fact]
+    public void AuditLogEntry_WithRequiredFields_CreatesSuccessfully()
+    {
+        var entry = new AuditLogEntry
+        {
+            EntityType = "Content",
+            EntityId = Guid.NewGuid(),
+            Action = "StatusChanged",
+            Timestamp = DateTimeOffset.UtcNow,
+        };
+
+        Assert.Equal("Content", entry.EntityType);
+        Assert.Equal("StatusChanged", entry.Action);
+        Assert.NotEqual(Guid.Empty, entry.Id);
+    }
+
+    [Fact]
+    public void OldValue_And_NewValue_AcceptNull()
+    {
+        var entry = new AuditLogEntry
+        {
+            EntityType = "Content",
+            EntityId = Guid.NewGuid(),
+            Action = "Created",
+            Timestamp = DateTimeOffset.UtcNow,
+            OldValue = null,
+            NewValue = null,
+        };
+
+        Assert.Null(entry.OldValue);
+        Assert.Null(entry.NewValue);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/BrandProfileTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/BrandProfileTests.cs
new file mode 100644
index 0000000..3e55e2b
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/BrandProfileTests.cs
@@ -0,0 +1,32 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class BrandProfileTests
+{
+    [Fact]
+    public void BrandProfile_WithValidFields_CreatesSuccessfully()
+    {
+        var profile = new BrandProfile
+        {
+            Name = "Tech Thought Leader",
+            StyleGuidelines = "Professional, approachable",
+            PersonaDescription = "Senior engineer sharing insights",
+            IsActive = true,
+        };
+
+        Assert.Equal("Tech Thought Leader", profile.Name);
+        Assert.NotEqual(Guid.Empty, profile.Id);
+    }
+
+    [Fact]
+    public void ToneDescriptors_And_Topics_InitializeAsEmptyLists()
+    {
+        var profile = new BrandProfile();
+
+        Assert.NotNull(profile.ToneDescriptors);
+        Assert.Empty(profile.ToneDescriptors);
+        Assert.NotNull(profile.Topics);
+        Assert.Empty(profile.Topics);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs
new file mode 100644
index 0000000..75e2754
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs
@@ -0,0 +1,54 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class ContentCalendarSlotTests
+{
+    [Fact]
+    public void Slot_WithValidTimeZoneId_CreatesSuccessfully()
+    {
+        var slot = new ContentCalendarSlot
+        {
+            ScheduledDate = new DateOnly(2026, 3, 15),
+            TimeZoneId = "America/New_York",
+            ContentType = ContentType.BlogPost,
+            TargetPlatform = PlatformType.LinkedIn,
+        };
+
+        Assert.Equal("America/New_York", slot.TimeZoneId);
+        Assert.NotEqual(Guid.Empty, slot.Id);
+    }
+
+    [Fact]
+    public void Slot_WithRecurrencePattern_StoresCronString()
+    {
+        var slot = new ContentCalendarSlot
+        {
+            ScheduledDate = new DateOnly(2026, 3, 15),
+            TimeZoneId = "UTC",
+            ContentType = ContentType.SocialPost,
+            TargetPlatform = PlatformType.TwitterX,
+            IsRecurring = true,
+            RecurrencePattern = "0 9 * * 1-5",
+        };
+
+        Assert.True(slot.IsRecurring);
+        Assert.Equal("0 9 * * 1-5", slot.RecurrencePattern);
+    }
+
+    [Fact]
+    public void NonRecurringSlot_HasNullRecurrencePattern()
+    {
+        var slot = new ContentCalendarSlot
+        {
+            ScheduledDate = new DateOnly(2026, 3, 15),
+            TimeZoneId = "UTC",
+            ContentType = ContentType.SocialPost,
+            TargetPlatform = PlatformType.TwitterX,
+        };
+
+        Assert.False(slot.IsRecurring);
+        Assert.Null(slot.RecurrencePattern);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs
new file mode 100644
index 0000000..7fbd5a8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs
@@ -0,0 +1,110 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.Events;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class ContentTests
+{
+    private static Content CreateDraft() =>
+        Content.Create(ContentType.BlogPost, "Test body");
+
+    [Fact]
+    public void NewContent_DefaultsToDraftStatus()
+    {
+        var content = CreateDraft();
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+
+    [Theory]
+    [InlineData(ContentStatus.Draft, ContentStatus.Review, true)]
+    [InlineData(ContentStatus.Draft, ContentStatus.Archived, true)]
+    [InlineData(ContentStatus.Draft, ContentStatus.Published, false)]
+    [InlineData(ContentStatus.Review, ContentStatus.Draft, true)]
+    [InlineData(ContentStatus.Review, ContentStatus.Approved, true)]
+    [InlineData(ContentStatus.Approved, ContentStatus.Scheduled, true)]
+    [InlineData(ContentStatus.Approved, ContentStatus.Draft, true)]
+    [InlineData(ContentStatus.Scheduled, ContentStatus.Publishing, true)]
+    [InlineData(ContentStatus.Scheduled, ContentStatus.Draft, true)]
+    [InlineData(ContentStatus.Publishing, ContentStatus.Published, true)]
+    [InlineData(ContentStatus.Publishing, ContentStatus.Failed, true)]
+    [InlineData(ContentStatus.Publishing, ContentStatus.Draft, false)]
+    [InlineData(ContentStatus.Published, ContentStatus.Archived, true)]
+    [InlineData(ContentStatus.Published, ContentStatus.Draft, false)]
+    [InlineData(ContentStatus.Failed, ContentStatus.Draft, true)]
+    [InlineData(ContentStatus.Failed, ContentStatus.Archived, true)]
+    [InlineData(ContentStatus.Archived, ContentStatus.Draft, true)]
+    [InlineData(ContentStatus.Archived, ContentStatus.Published, false)]
+    public void TransitionTo_ValidatesStateTransitions(
+        ContentStatus from, ContentStatus to, bool shouldSucceed)
+    {
+        var content = CreateDraft();
+        TransitionToState(content, from);
+
+        if (shouldSucceed)
+        {
+            content.TransitionTo(to);
+            Assert.Equal(to, content.Status);
+        }
+        else
+        {
+            Assert.Throws<InvalidOperationException>(() => content.TransitionTo(to));
+        }
+    }
+
+    [Fact]
+    public void TransitionTo_RaisesContentStateChangedEvent()
+    {
+        var content = CreateDraft();
+        content.TransitionTo(ContentStatus.Review);
+
+        var domainEvent = Assert.Single(content.DomainEvents);
+        var stateChanged = Assert.IsType<ContentStateChangedEvent>(domainEvent);
+        Assert.Equal(content.Id, stateChanged.ContentId);
+        Assert.Equal(ContentStatus.Draft, stateChanged.OldStatus);
+        Assert.Equal(ContentStatus.Review, stateChanged.NewStatus);
+    }
+
+    [Fact]
+    public void Content_WithMultipleTargetPlatforms_StoresCorrectly()
+    {
+        var platforms = new[] { PlatformType.TwitterX, PlatformType.LinkedIn };
+        var content = Content.Create(ContentType.SocialPost, "Post", targetPlatforms: platforms);
+
+        Assert.Equal(2, content.TargetPlatforms.Length);
+        Assert.Contains(PlatformType.TwitterX, content.TargetPlatforms);
+        Assert.Contains(PlatformType.LinkedIn, content.TargetPlatforms);
+    }
+
+    [Fact]
+    public void Content_WithEmptyTargetPlatforms_IsValid()
+    {
+        var content = Content.Create(ContentType.SocialPost, "Post");
+        Assert.Empty(content.TargetPlatforms);
+    }
+
+    private static void TransitionToState(Content content, ContentStatus target)
+    {
+        if (content.Status == target) return;
+
+        var path = target switch
+        {
+            ContentStatus.Draft => Array.Empty<ContentStatus>(),
+            ContentStatus.Review => new[] { ContentStatus.Review },
+            ContentStatus.Approved => new[] { ContentStatus.Review, ContentStatus.Approved },
+            ContentStatus.Scheduled => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled },
+            ContentStatus.Publishing => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing },
+            ContentStatus.Published => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published },
+            ContentStatus.Failed => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Failed },
+            ContentStatus.Archived => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published, ContentStatus.Archived },
+            _ => throw new ArgumentOutOfRangeException(nameof(target))
+        };
+
+        foreach (var step in path)
+        {
+            content.TransitionTo(step);
+        }
+
+        content.ClearDomainEvents();
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs
new file mode 100644
index 0000000..5293094
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs
@@ -0,0 +1,38 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class PlatformTests
+{
+    [Fact]
+    public void Platform_WithAllRequiredFields_CreatesSuccessfully()
+    {
+        var platform = new Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter/X",
+            IsConnected = true,
+        };
+
+        Assert.Equal(PlatformType.TwitterX, platform.Type);
+        Assert.Equal("Twitter/X", platform.DisplayName);
+        Assert.True(platform.IsConnected);
+        Assert.NotEqual(Guid.Empty, platform.Id);
+    }
+
+    [Fact]
+    public void EncryptedTokens_AreByteArrays()
+    {
+        var platform = new Platform
+        {
+            Type = PlatformType.LinkedIn,
+            DisplayName = "LinkedIn",
+            EncryptedAccessToken = new byte[] { 1, 2, 3 },
+            EncryptedRefreshToken = new byte[] { 4, 5, 6 },
+        };
+
+        Assert.IsType<byte[]>(platform.EncryptedAccessToken);
+        Assert.IsType<byte[]>(platform.EncryptedRefreshToken);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/UserTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/UserTests.cs
new file mode 100644
index 0000000..ec45259
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/UserTests.cs
@@ -0,0 +1,27 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class UserTests
+{
+    [Fact]
+    public void User_WithValidTimeZoneId_CreatesSuccessfully()
+    {
+        var user = new User
+        {
+            Email = "user@example.com",
+            DisplayName = "Test User",
+            TimeZoneId = "America/New_York",
+        };
+
+        Assert.Equal("America/New_York", user.TimeZoneId);
+        Assert.NotEqual(Guid.Empty, user.Id);
+    }
+
+    [Fact]
+    public void Settings_IsNotNullByDefault()
+    {
+        var user = new User();
+        Assert.NotNull(user.Settings);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
new file mode 100644
index 0000000..fec6f32
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
@@ -0,0 +1,46 @@
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Enums;
+
+public class EnumTests
+{
+    [Fact]
+    public void ContentType_HasExactly4Values()
+    {
+        var values = Enum.GetValues<ContentType>();
+        Assert.Equal(4, values.Length);
+        Assert.Contains(ContentType.BlogPost, values);
+        Assert.Contains(ContentType.SocialPost, values);
+        Assert.Contains(ContentType.Thread, values);
+        Assert.Contains(ContentType.VideoDescription, values);
+    }
+
+    [Fact]
+    public void ContentStatus_HasExactly8Values()
+    {
+        var values = Enum.GetValues<ContentStatus>();
+        Assert.Equal(8, values.Length);
+    }
+
+    [Fact]
+    public void PlatformType_HasExactly4Values()
+    {
+        var values = Enum.GetValues<PlatformType>();
+        Assert.Equal(4, values.Length);
+        Assert.Contains(PlatformType.TwitterX, values);
+        Assert.Contains(PlatformType.LinkedIn, values);
+        Assert.Contains(PlatformType.Instagram, values);
+        Assert.Contains(PlatformType.YouTube, values);
+    }
+
+    [Fact]
+    public void AutonomyLevel_HasExactly4Values()
+    {
+        var values = Enum.GetValues<AutonomyLevel>();
+        Assert.Equal(4, values.Length);
+        Assert.Contains(AutonomyLevel.Manual, values);
+        Assert.Contains(AutonomyLevel.Assisted, values);
+        Assert.Contains(AutonomyLevel.SemiAuto, values);
+        Assert.Contains(AutonomyLevel.Autonomous, values);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/ValueObjects/ContentMetadataTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/ValueObjects/ContentMetadataTests.cs
new file mode 100644
index 0000000..fbe7f01
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/ValueObjects/ContentMetadataTests.cs
@@ -0,0 +1,47 @@
+using PersonalBrandAssistant.Domain.ValueObjects;
+
+namespace PersonalBrandAssistant.Domain.Tests.ValueObjects;
+
+public class ContentMetadataTests
+{
+    [Fact]
+    public void ContentMetadata_WithAllFields_CreatesValidObject()
+    {
+        var metadata = new ContentMetadata
+        {
+            Tags = ["ai", "branding"],
+            SeoKeywords = ["personal brand"],
+            PlatformSpecificData = new Dictionary<string, string> { ["twitter"] = "thread" },
+            AiGenerationContext = "Generated by Claude",
+            TokensUsed = 1500,
+            EstimatedCost = 0.05m,
+        };
+
+        Assert.Equal(2, metadata.Tags.Count);
+        Assert.Single(metadata.SeoKeywords);
+        Assert.Equal("Generated by Claude", metadata.AiGenerationContext);
+        Assert.Equal(1500, metadata.TokensUsed);
+        Assert.Equal(0.05m, metadata.EstimatedCost);
+    }
+
+    [Fact]
+    public void ContentMetadata_WithNullOptionalFields_IsValid()
+    {
+        var metadata = new ContentMetadata();
+
+        Assert.Null(metadata.AiGenerationContext);
+        Assert.Null(metadata.TokensUsed);
+        Assert.Null(metadata.EstimatedCost);
+    }
+
+    [Fact]
+    public void Tags_And_SeoKeywords_InitializeAsEmptyLists()
+    {
+        var metadata = new ContentMetadata();
+
+        Assert.NotNull(metadata.Tags);
+        Assert.Empty(metadata.Tags);
+        Assert.NotNull(metadata.SeoKeywords);
+        Assert.Empty(metadata.SeoKeywords);
+    }
+}
