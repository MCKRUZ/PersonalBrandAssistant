diff --git a/src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs b/src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs
new file mode 100644
index 0000000..9b7fed9
--- /dev/null
+++ b/src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs
@@ -0,0 +1,74 @@
+using PBA.Domain.Enums;
+using Stateless;
+
+namespace PBA.Application.Features.ContentStudio;
+
+public static class ContentStateMachine
+{
+    public static StateMachine<ContentStatus, ContentTrigger> Create(Domain.Entities.Content content)
+    {
+        var machine = new StateMachine<ContentStatus, ContentTrigger>(
+            () => content.Status,
+            s => content.Status = s);
+
+        machine.Configure(ContentStatus.Idea)
+            .Permit(ContentTrigger.StartDraft, ContentStatus.Draft);
+
+        machine.Configure(ContentStatus.Draft)
+            .PermitIf(ContentTrigger.SubmitForReview, ContentStatus.Review,
+                () => !string.IsNullOrWhiteSpace(content.Body))
+            .PermitIf(ContentTrigger.Approve, ContentStatus.Approved,
+                () => !string.IsNullOrWhiteSpace(content.Body))
+            .Permit(ContentTrigger.Archive, ContentStatus.Archived);
+
+        machine.Configure(ContentStatus.Review)
+            .Permit(ContentTrigger.Approve, ContentStatus.Approved)
+            .Permit(ContentTrigger.RequestChanges, ContentStatus.Draft)
+            .Permit(ContentTrigger.Archive, ContentStatus.Archived);
+
+        machine.Configure(ContentStatus.Approved)
+            .PermitIf(ContentTrigger.Schedule, ContentStatus.Scheduled,
+                () => content.ScheduledAt.HasValue && content.ScheduledAt > DateTimeOffset.UtcNow)
+            .Permit(ContentTrigger.PublishNow, ContentStatus.Published);
+
+        machine.Configure(ContentStatus.Scheduled)
+            .Permit(ContentTrigger.Publish, ContentStatus.Published)
+            .Permit(ContentTrigger.Unschedule, ContentStatus.Approved);
+
+        machine.Configure(ContentStatus.Published)
+            .OnEntryAsync(_ =>
+            {
+                content.PublishedAt = DateTimeOffset.UtcNow;
+                content.UpdatedAt = DateTimeOffset.UtcNow;
+                return Task.CompletedTask;
+            })
+            .Permit(ContentTrigger.Archive, ContentStatus.Archived)
+            .Permit(ContentTrigger.Unpublish, ContentStatus.Draft);
+
+        machine.Configure(ContentStatus.Scheduled)
+            .OnEntryAsync(_ =>
+            {
+                content.UpdatedAt = DateTimeOffset.UtcNow;
+                return Task.CompletedTask;
+            });
+
+        machine.Configure(ContentStatus.Archived)
+            .OnEntryAsync(_ =>
+            {
+                content.UpdatedAt = DateTimeOffset.UtcNow;
+                return Task.CompletedTask;
+            })
+            .Permit(ContentTrigger.Restore, ContentStatus.Draft);
+
+        machine.Configure(ContentStatus.Draft)
+            .OnEntryAsync(_ =>
+            {
+                content.ScheduledAt = null;
+                content.HangfireJobId = null;
+                content.UpdatedAt = DateTimeOffset.UtcNow;
+                return Task.CompletedTask;
+            });
+
+        return machine;
+    }
+}
diff --git a/src/PBA.Application/PBA.Application.csproj b/src/PBA.Application/PBA.Application.csproj
index da2b669..49b3c69 100644
--- a/src/PBA.Application/PBA.Application.csproj
+++ b/src/PBA.Application/PBA.Application.csproj
@@ -16,6 +16,7 @@
     <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.12.0" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
     <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.7" />
+    <PackageReference Include="Stateless" Version="5.20.1" />
   </ItemGroup>
 
   <ItemGroup>
diff --git a/src/PBA.Domain/Enums/ContentTrigger.cs b/src/PBA.Domain/Enums/ContentTrigger.cs
new file mode 100644
index 0000000..ad3a123
--- /dev/null
+++ b/src/PBA.Domain/Enums/ContentTrigger.cs
@@ -0,0 +1,16 @@
+namespace PBA.Domain.Enums;
+
+public enum ContentTrigger
+{
+    StartDraft,
+    SubmitForReview,
+    Approve,
+    RequestChanges,
+    Schedule,
+    Unschedule,
+    PublishNow,
+    Publish,
+    Archive,
+    Restore,
+    Unpublish
+}
diff --git a/tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs b/tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs
new file mode 100644
index 0000000..65de4ae
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs
@@ -0,0 +1,244 @@
+using PBA.Application.Features.ContentStudio;
+using PBA.Domain.Enums;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.Content;
+
+public class ContentStateMachineTests
+{
+    private static Domain.Entities.Content CreateContent(
+        ContentStatus status = ContentStatus.Idea,
+        string body = "",
+        DateTimeOffset? scheduledAt = null)
+    {
+        return new Domain.Entities.Content
+        {
+            Title = "Test",
+            Status = status,
+            Body = body,
+            ScheduledAt = scheduledAt
+        };
+    }
+
+    [Fact]
+    public async Task Fire_StartDraft_FromIdea_TransitionsToDraft()
+    {
+        var content = CreateContent(ContentStatus.Idea);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.StartDraft);
+
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_SubmitForReview_FromDraft_TransitionsToReview()
+    {
+        var content = CreateContent(ContentStatus.Draft, body: "some content");
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.SubmitForReview);
+
+        Assert.Equal(ContentStatus.Review, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Approve_FromDraft_TransitionsToApproved()
+    {
+        var content = CreateContent(ContentStatus.Draft, body: "some content");
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Approve);
+
+        Assert.Equal(ContentStatus.Approved, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Archive_FromDraft_TransitionsToArchived()
+    {
+        var content = CreateContent(ContentStatus.Draft);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Archive);
+
+        Assert.Equal(ContentStatus.Archived, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Approve_FromReview_TransitionsToApproved()
+    {
+        var content = CreateContent(ContentStatus.Review);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Approve);
+
+        Assert.Equal(ContentStatus.Approved, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_RequestChanges_FromReview_TransitionsToDraft()
+    {
+        var content = CreateContent(ContentStatus.Review);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.RequestChanges);
+
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Archive_FromReview_TransitionsToArchived()
+    {
+        var content = CreateContent(ContentStatus.Review);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Archive);
+
+        Assert.Equal(ContentStatus.Archived, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Schedule_FromApproved_TransitionsToScheduled()
+    {
+        var content = CreateContent(ContentStatus.Approved, scheduledAt: DateTimeOffset.UtcNow.AddDays(1));
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Schedule);
+
+        Assert.Equal(ContentStatus.Scheduled, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_PublishNow_FromApproved_TransitionsToPublished()
+    {
+        var content = CreateContent(ContentStatus.Approved);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.PublishNow);
+
+        Assert.Equal(ContentStatus.Published, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Publish_FromScheduled_TransitionsToPublished()
+    {
+        var content = CreateContent(ContentStatus.Scheduled);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Publish);
+
+        Assert.Equal(ContentStatus.Published, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Unschedule_FromScheduled_TransitionsToApproved()
+    {
+        var content = CreateContent(ContentStatus.Scheduled);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Unschedule);
+
+        Assert.Equal(ContentStatus.Approved, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Archive_FromPublished_TransitionsToArchived()
+    {
+        var content = CreateContent(ContentStatus.Published);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Archive);
+
+        Assert.Equal(ContentStatus.Archived, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Unpublish_FromPublished_TransitionsToDraft()
+    {
+        var content = CreateContent(ContentStatus.Published);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Unpublish);
+
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_Restore_FromArchived_TransitionsToDraft()
+    {
+        var content = CreateContent(ContentStatus.Archived);
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Restore);
+
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+
+    [Fact]
+    public async Task Fire_SubmitForReview_FromDraft_FailsWhenBodyEmpty()
+    {
+        var content = CreateContent(ContentStatus.Draft, body: "");
+        var machine = ContentStateMachine.Create(content);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => machine.FireAsync(ContentTrigger.SubmitForReview));
+    }
+
+    [Fact]
+    public async Task Fire_Approve_FromDraft_FailsWhenBodyEmpty()
+    {
+        var content = CreateContent(ContentStatus.Draft, body: "");
+        var machine = ContentStateMachine.Create(content);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => machine.FireAsync(ContentTrigger.Approve));
+    }
+
+    [Fact]
+    public async Task Fire_Schedule_FromApproved_FailsWhenScheduledAtNull()
+    {
+        var content = CreateContent(ContentStatus.Approved, scheduledAt: null);
+        var machine = ContentStateMachine.Create(content);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => machine.FireAsync(ContentTrigger.Schedule));
+    }
+
+    [Fact]
+    public async Task Fire_InvalidTransition_IdeaToPublished_Throws()
+    {
+        var content = CreateContent(ContentStatus.Idea);
+        var machine = ContentStateMachine.Create(content);
+
+        await Assert.ThrowsAsync<InvalidOperationException>(
+            () => machine.FireAsync(ContentTrigger.PublishNow));
+    }
+
+    [Fact]
+    public async Task Fire_Publish_SetsPublishedAtAndUpdatedAt()
+    {
+        var content = CreateContent(ContentStatus.Approved);
+        var beforeFire = content.UpdatedAt;
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.PublishNow);
+
+        Assert.NotNull(content.PublishedAt);
+        Assert.True(content.UpdatedAt >= beforeFire);
+    }
+
+    [Fact]
+    public async Task Fire_Unpublish_ClearsScheduledAtAndHangfireJobId()
+    {
+        var content = CreateContent(ContentStatus.Published);
+        content.ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1);
+        content.HangfireJobId = "job1";
+        var machine = ContentStateMachine.Create(content);
+
+        await machine.FireAsync(ContentTrigger.Unpublish);
+
+        Assert.Null(content.ScheduledAt);
+        Assert.Null(content.HangfireJobId);
+        Assert.Equal(ContentStatus.Draft, content.Status);
+    }
+}
diff --git a/tests/PBA.Application.Tests/PBA.Application.Tests.csproj b/tests/PBA.Application.Tests/PBA.Application.Tests.csproj
index 56cf851..5d9ed5f 100644
--- a/tests/PBA.Application.Tests/PBA.Application.Tests.csproj
+++ b/tests/PBA.Application.Tests/PBA.Application.Tests.csproj
@@ -14,6 +14,7 @@
     <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.7" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
     <PackageReference Include="Moq" Version="4.20.72" />
+    <PackageReference Include="Stateless" Version="5.20.1" />
     <PackageReference Include="xunit" Version="2.9.3" />
     <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
   </ItemGroup>
