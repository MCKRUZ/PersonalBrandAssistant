# Multi-Platform Publishing — TDD Plan

Testing framework: xUnit + Moq + EF Core InMemory (established patterns in codebase).
Test naming: `MethodName_Scenario_ExpectedResult`
Test location: `tests/PBA.Application.Tests/` and `tests/PBA.Infrastructure.Tests/`

---

## Phase 1: Foundation

### IPlatformConnector Interface + Supporting Types

No tests needed — these are interfaces and records (type definitions only).

### PlatformCredential Entity + Migration

```csharp
// Test: PlatformCredential can be persisted and retrieved via EF Core InMemory
// Test: Only one active credential per platform (query filter or business rule)
// Test: TargetPlatforms JSON column serializes/deserializes List<Platform> correctly on Content entity
// Test: ContentPlatformPublish retry fields (RetryCount, NextRetryAt) default correctly
```

### IContentTransformer + IPlatformFormatter

```csharp
// Test: ContentTransformer_WithBlogPlatform_DelegatesToBlogFormatter
// Test: ContentTransformer_WithUnknownPlatform_ThrowsNotSupportedException
// Test: ContentTransformer_StripsFrontmatter_BeforeDelegating (shared preprocessor)
// Test: ContentTransformer_ResolvesRelativeImagePaths_ToAbsoluteUrls
```

### BlogFormatter

```csharp
// Test: BlogFormatter_ConvertsMarkdownToHtml_ViaMarkdig
// Test: BlogFormatter_AppliesHtmlTemplate_WithTokenReplacement
// Test: BlogFormatter_GeneratesUrlSlug_FromTitle
// Test: BlogFormatter_HandlesSpecialCharactersInTitle_ForSlugGeneration
```

### BlogConnector Migration

```csharp
// Test: BlogConnector_ImplementsIPlatformConnector
// Test: BlogConnector_PublishAsync_UsesTransformedContentDirectly (no internal Markdig)
// Test: BlogConnector_PublishAsync_ReturnsSuccessWithUrl
// Test: BlogConnector_PublishAsync_ReturnsFailureOnGitError
// Test: BlogConnector_GetCapabilities_ReturnsCorrectLimits
// Test: BlogConnector_ValidateCredentialsAsync_ChecksRepoPathExists
```

### ContentPublisher Refactor

```csharp
// Test: PublishAsync_ResolvesConnectorByPlatform_ViaKeyedDI
// Test: PublishAsync_PrimaryFails_AbortsWithoutPublishingSecondaries
// Test: PublishAsync_PrimarySucceeds_FiresStateMachineTrigger
// Test: PublishAsync_SecondaryFails_CreatesFailedContentPlatformPublishRecord
// Test: PublishAsync_SecondaryFails_SchedulesRetryJob
// Test: PublishAsync_SkipsPlatformWithExistingPublishedRecord (idempotency)
// Test: PublishAsync_NoTargetPlatforms_UsesContentTargetPlatforms
// Test: PublishAsync_NoContentTargetPlatforms_UsesPrimaryPlatformOnly
// Test: PublishAsync_GuidOverload_CallsFullMethodWithNullTargets (Hangfire compat)
// Test: PublishAsync_ParallelSecondaries_AllPublishIndependently
```

### Update Existing Tests

```csharp
// Migrate: BlogConnectorTests to use IPlatformConnector interface
// Migrate: Any test mocking IBlogConnector to mock IPlatformConnector
// Migrate: ContentPublisher tests to expect keyed DI resolution
// Verify: ContentStateMachine tests still pass unchanged
```

---

## Phase 2: OAuth + Credential Storage

### ITokenEncryptor

```csharp
// Test: Encrypt_ThenDecrypt_ReturnsOriginalValue
// Test: Encrypt_ProducesDifferentCiphertextEachTime (unique IV/nonce)
// Test: Decrypt_WithWrongKey_ThrowsCryptographicException
// Test: Encrypt_NullInput_ThrowsArgumentNullException
// Test: Decrypt_CorruptedCiphertext_ThrowsCryptographicException
```

### IOAuthService

```csharp
// Test: GetAuthorizationUrl_LinkedIn_ReturnsCorrectUrlWithScopes
// Test: GetAuthorizationUrl_Twitter_IncludesPKCECodeChallenge
// Test: GetAuthorizationUrl_IncludesStateParameter
// Test: ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens
// Test: ExchangeCodeAsync_Twitter_UsesCodeVerifierForPKCE
// Test: ExchangeCodeAsync_InvalidState_ThrowsSecurityException
// Test: RefreshTokenAsync_LinkedIn_UpdatesStoredTokens
// Test: RefreshTokenAsync_Twitter_HandlesShortLivedTokens
// Test: RefreshTokenAsync_ExpiredRefreshToken_ReturnsFailure
```

### OAuth Endpoints

```csharp
// Test: Authorize_LinkedIn_RedirectsToLinkedInAuthUrl
// Test: Authorize_Twitter_RedirectsToTwitterAuthUrl
// Test: Authorize_UnsupportedPlatform_Returns400
// Test: Callback_ValidCode_StoresCredentialAndRedirects
// Test: Callback_InvalidState_Returns403
// Test: Status_ConnectedPlatform_ReturnsConnectedWithExpiry
// Test: Status_ExpiredPlatform_ReturnsExpiredStatus
// Test: Status_NotConfigured_ReturnsNotConfiguredStatus
// Test: Delete_ConnectedPlatform_RemovesCredential
```

### Platform Management Endpoints

```csharp
// Test: GetPlatforms_ReturnsAllPlatformsWithConnectionStatus
// Test: PostCredentials_Medium_StoresEncryptedIntegrationToken
// Test: PostCredentials_Substack_LoginAndStoresCookies
// Test: PostCredentials_InvalidToken_ReturnsValidationError
```

---

## Phase 3: Connectors

### MediumConnector + MediumFormatter

```csharp
// MediumFormatter
// Test: Format_InjectsCanonicalUrlFooter
// Test: Format_ResolvesRelativeImageUrls_ToAbsolute
// Test: Format_ConvertsSvgReferences_ToPng
// Test: Format_PreservesMarkdownFormat (Medium accepts markdown)

// MediumConnector
// Test: PublishAsync_Draft_SendsCorrectPayload (publishStatus=draft)
// Test: PublishAsync_Public_SendsCorrectPayload (publishStatus=public)
// Test: PublishAsync_TruncatesTags_ToMax3
// Test: PublishAsync_IncludesCanonicalUrl
// Test: PublishAsync_ReturnsPublishedUrlFromResponse
// Test: PublishAsync_InvalidToken_ReturnsFailureResult
// Test: PublishAsync_RateLimited_ReturnsFailureWithRetryHint
// Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
// Test: ValidateCredentialsAsync_InvalidToken_ReturnsFalse
// Test: GetCapabilities_ReturnsCorrectValues
```

### LinkedInConnector + LinkedInFormatter

```csharp
// LinkedInFormatter
// Test: Format_StripMarkdown_ToPlainText
// Test: Format_TruncatesTo3000Chars_WithEllipsis
// Test: Format_PreservesLineBreaksAndBullets
// Test: Format_AddsReadMoreLink_WhenTruncated

// LinkedInConnector
// Test: PublishAsync_TextPost_CreatesCorrectPayload
// Test: PublishAsync_WithImage_UploadsImageFirst
// Test: PublishAsync_WithArticleLink_IncludesContentObject
// Test: PublishAsync_ExpiredToken_RefreshesBeforePublishing
// Test: PublishAsync_RefreshFails_ReturnsAuthFailure
// Test: PublishAsync_IncludesVersionHeader
// Test: PublishAsync_ReturnsPostUrnFromResponseHeader
// Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
// Test: GetCapabilities_ReturnsCorrectValues
```

### TwitterConnector + TwitterFormatter

```csharp
// TwitterFormatter
// Test: Format_Under280Chars_ReturnsSingleSegment
// Test: Format_Over280Chars_SplitsIntoThreadSegments
// Test: Format_SplitsAtSentenceBoundaries
// Test: Format_IncludesArticleLinkInFirstOrLastSegment
// Test: Format_StripMarkdown_ToPlainText
// Test: Format_PreservesHashtags

// TwitterConnector
// Test: PublishAsync_SingleTweet_PostsOnce
// Test: PublishAsync_Thread_ChainsRepliesWithCorrectIds
// Test: PublishAsync_WithMedia_UploadsViaChunkedProcess
// Test: PublishAsync_MediaFinalize_PollsUntilProcessingComplete
// Test: PublishAsync_ExpiredToken_RefreshesBeforePublishing
// Test: PublishAsync_ReturnsFirstTweetIdForThreads
// Test: ValidateCredentialsAsync_ValidToken_ReturnsTrue
// Test: GetCapabilities_ReturnsCorrectValues
```

### SubstackConnector + SubstackFormatter

```csharp
// SubstackFormatter
// Test: Format_ConvertsMarkdownToTiptapJson
// Test: Format_Paragraph_MapsToParagraphNode
// Test: Format_Heading_MapsToHeadingNodeWithLevel
// Test: Format_BulletList_MapsToBulletListNode
// Test: Format_Image_MapsToCaptionedImageNode
// Test: Format_CodeBlock_MapsToCodeBlockNode
// Test: Format_BoldText_AddsMarkToTextNode
// Test: Format_InjectsSubscribeWidget_AfterExecutiveSummary
// Test: Format_StripsReferencesSection
// Test: Format_StripsAuthorBio

// SubstackConnector
// Test: PublishAsync_Draft_CreatesDraftOnly
// Test: PublishAsync_Publish_CreatesDraftThenPublishes
// Test: PublishAsync_UploadImages_ReplacesUrlsWithCdnUrls
// Test: PublishAsync_ExpiredCookies_ReturnsAuthFailure (no auto-relogin)
// Test: PublishAsync_AddsTags_AfterDraftCreation
// Test: PublishAsync_ReturnsPublishedUrl
// Test: ValidateCredentialsAsync_ValidCookies_ReturnsTrue
// Test: ValidateCredentialsAsync_ExpiredCookies_ReturnsFalse
// Test: GetCapabilities_ReturnsCorrectValues
```

---

## Phase 4: Retry + API Updates

### Retry Handler

```csharp
// Test: RetryAsync_PublishedRecord_SkipsWithoutPublishing (idempotency)
// Test: RetryAsync_Success_UpdatesRecordToPublished
// Test: RetryAsync_Failure_IncrementsRetryCount
// Test: RetryAsync_UnderMaxRetries_SchedulesNextRetry
// Test: RetryAsync_AtMaxRetries_MarksAsPermanentlyFailed
// Test: RetryAsync_BackoffIncreases_5min_30min_2hours
```

### Content Endpoint Updates

```csharp
// Test: Publish_WithTargetPlatforms_PassesPlatformsToPublisher
// Test: Publish_WithoutTargetPlatforms_UsesContentDefaults
// Test: Schedule_WithTargetPlatforms_StoresPlatformsForScheduledPublish
// Test: Retry_ValidPlatform_TriggersRetryForSpecificPlatform
// Test: Retry_NotFailedPlatform_Returns400
// Test: PublishStatus_ReturnsPerPlatformStatus
```

---

## Phase 5: Frontend

Frontend tests use Jasmine/Karma (Angular test framework).

### Platform Connections Page

```typescript
// Test: should show connect button for disconnected platforms
// Test: should show connected status with expiry for OAuth platforms
// Test: should call authorize endpoint when connect clicked (LinkedIn/Twitter)
// Test: should submit token form for Medium
// Test: should submit login form for Substack
// Test: should call delete endpoint when disconnect clicked
// Test: should refresh status after connection change
```

### Content Editor Platform Targets

```typescript
// Test: should show checkboxes for all configured platforms
// Test: should disable unconfigured/disconnected platforms
// Test: should update content.targetPlatforms on selection change
// Test: should show character count per platform
// Test: should highlight platforms exceeding character limit
```

### Publish Confirmation Modal

```typescript
// Test: should display per-platform preview
// Test: should show connection status per platform
// Test: should allow toggling platforms before confirming
// Test: should call publish endpoint with selected platforms
// Test: should disable confirm button if primary platform not selected
```

### Content List Status Badges

```typescript
// Test: should show green badge for published platforms
// Test: should show red badge with retry action for failed platforms
// Test: should show pending badge for in-progress publishes
```
