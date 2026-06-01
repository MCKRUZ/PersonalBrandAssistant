diff --git a/src/PBA.Application/Common/Interfaces/IContentTransformer.cs b/src/PBA.Application/Common/Interfaces/IContentTransformer.cs
new file mode 100644
index 0000000..5ee8464
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IContentTransformer.cs
@@ -0,0 +1,9 @@
+namespace PBA.Application.Common.Interfaces;
+
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+public interface IContentTransformer
+{
+    Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct);
+}
diff --git a/src/PBA.Application/Common/Interfaces/IPlatformConnector.cs b/src/PBA.Application/Common/Interfaces/IPlatformConnector.cs
new file mode 100644
index 0000000..8493415
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IPlatformConnector.cs
@@ -0,0 +1,12 @@
+namespace PBA.Application.Common.Interfaces;
+
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+public interface IPlatformConnector
+{
+    Platform Platform { get; }
+    Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct);
+    Task<bool> ValidateCredentialsAsync(CancellationToken ct);
+    PlatformCapabilities GetCapabilities();
+}
diff --git a/src/PBA.Application/Common/Interfaces/IPlatformFormatter.cs b/src/PBA.Application/Common/Interfaces/IPlatformFormatter.cs
new file mode 100644
index 0000000..94ab192
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IPlatformFormatter.cs
@@ -0,0 +1,10 @@
+namespace PBA.Application.Common.Interfaces;
+
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
+public interface IPlatformFormatter
+{
+    Platform Platform { get; }
+    Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct);
+}
diff --git a/src/PBA.Application/Common/Models/ImageReference.cs b/src/PBA.Application/Common/Models/ImageReference.cs
new file mode 100644
index 0000000..7b1a2a1
--- /dev/null
+++ b/src/PBA.Application/Common/Models/ImageReference.cs
@@ -0,0 +1,7 @@
+namespace PBA.Application.Common.Models;
+
+public record ImageReference(
+    string OriginalPath,
+    string AbsoluteUrl,
+    string? AltText
+);
diff --git a/src/PBA.Application/Common/Models/PlatformCapabilities.cs b/src/PBA.Application/Common/Models/PlatformCapabilities.cs
new file mode 100644
index 0000000..af3708e
--- /dev/null
+++ b/src/PBA.Application/Common/Models/PlatformCapabilities.cs
@@ -0,0 +1,11 @@
+namespace PBA.Application.Common.Models;
+
+public record PlatformCapabilities(
+    int MaxCharacters,
+    bool SupportsMarkdown,
+    bool SupportsHtml,
+    bool SupportsImages,
+    bool SupportsScheduling,
+    bool SupportsThreads,
+    IReadOnlyList<string> SupportedMediaTypes
+);
diff --git a/src/PBA.Application/Common/Models/PlatformPublishRequest.cs b/src/PBA.Application/Common/Models/PlatformPublishRequest.cs
new file mode 100644
index 0000000..c47d5d1
--- /dev/null
+++ b/src/PBA.Application/Common/Models/PlatformPublishRequest.cs
@@ -0,0 +1,12 @@
+namespace PBA.Application.Common.Models;
+
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+public record PlatformPublishRequest(
+    Content Content,
+    string TransformedContent,
+    IReadOnlyList<string> Tags,
+    string? CanonicalUrl,
+    PublishMode Mode
+);
diff --git a/src/PBA.Application/Common/Models/PlatformPublishResult.cs b/src/PBA.Application/Common/Models/PlatformPublishResult.cs
new file mode 100644
index 0000000..1f8ca09
--- /dev/null
+++ b/src/PBA.Application/Common/Models/PlatformPublishResult.cs
@@ -0,0 +1,8 @@
+namespace PBA.Application.Common.Models;
+
+public record PlatformPublishResult(
+    bool Success,
+    string? PublishedUrl,
+    string? PlatformPostId,
+    string? ErrorMessage
+);
diff --git a/src/PBA.Application/Common/Models/PreprocessedContent.cs b/src/PBA.Application/Common/Models/PreprocessedContent.cs
new file mode 100644
index 0000000..aba841e
--- /dev/null
+++ b/src/PBA.Application/Common/Models/PreprocessedContent.cs
@@ -0,0 +1,9 @@
+namespace PBA.Application.Common.Models;
+
+public record PreprocessedContent(
+    string Title,
+    string Body,
+    string? CanonicalUrl,
+    IReadOnlyList<string> Tags,
+    IReadOnlyList<ImageReference> Images
+);
diff --git a/src/PBA.Application/Common/Models/PublishResult.cs b/src/PBA.Application/Common/Models/PublishResult.cs
new file mode 100644
index 0000000..7aae094
--- /dev/null
+++ b/src/PBA.Application/Common/Models/PublishResult.cs
@@ -0,0 +1,16 @@
+namespace PBA.Application.Common.Models;
+
+using PBA.Domain.Enums;
+
+public record PublishResult(
+    bool PrimarySuccess,
+    string? PrimaryUrl,
+    IReadOnlyList<PlatformPublishOutcome> SecondaryOutcomes
+);
+
+public record PlatformPublishOutcome(
+    Platform Platform,
+    bool Success,
+    string? Url,
+    string? Error
+);
diff --git a/src/PBA.Domain/Enums/Platform.cs b/src/PBA.Domain/Enums/Platform.cs
index 99063ff..e15e5fd 100644
--- a/src/PBA.Domain/Enums/Platform.cs
+++ b/src/PBA.Domain/Enums/Platform.cs
@@ -2,10 +2,11 @@ namespace PBA.Domain.Enums;
 
 public enum Platform
 {
-    Blog,
-    Substack,
-    LinkedIn,
-    Twitter,
-    Reddit,
-    YouTube
+    Blog = 0,
+    Substack = 1,
+    LinkedIn = 2,
+    Twitter = 3,
+    Reddit = 4,
+    YouTube = 5,
+    Medium = 6
 }
diff --git a/src/PBA.Domain/Enums/PublishMode.cs b/src/PBA.Domain/Enums/PublishMode.cs
new file mode 100644
index 0000000..f09d321
--- /dev/null
+++ b/src/PBA.Domain/Enums/PublishMode.cs
@@ -0,0 +1,8 @@
+namespace PBA.Domain.Enums;
+
+public enum PublishMode
+{
+    Draft,
+    Publish,
+    Schedule
+}
