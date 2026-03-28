# Section 08 -- Agent Capabilities: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-13
**Verdict:** WARNING -- Approve with required follow-ups

---

## Summary

This section implements 5 agent capabilities (Writer, Social, Repurpose, Engagement, Analytics) via a shared AgentCapabilityBase base class. The architecture is clean: a template-method pattern drives the shared execution flow, with virtual hooks for BuildVariables and BuildOutput. Only WriterAgentCapability overrides BuildOutput for title extraction; the other four rely entirely on the base class. The code compiles against the established IAgentCapability interface and Result of AgentOutput pattern correctly.

---

## Critical Issues

None found.

---

## Warnings (Should Fix)

### W-01: Exception message leaks internal details to callers

**File:** AgentCapabilityBase.cs:63
**Issue:** ex.Message is returned in the Result.Failure error string. If the IChatClient throws an exception with internal details (connection strings, SDK internals, model endpoint URLs), these propagate up through the Result to API consumers.

Current -- leaks exception internals:

    return Result<AgentOutput>.Failure(ErrorCode.InternalError,
        $"{Type} capability failed: {ex.Message}");

Fix -- generic message to caller, details only in logs:

    return Result<AgentOutput>.Failure(ErrorCode.InternalError,
        $"{Type} capability failed during execution. See logs for details.");

The _logger.LogError(ex, ...) on the line above already captures the full exception for diagnostics.

---

### W-02: Token counts are never populated in AgentOutput

**File:** AgentCapabilityBase.cs:83-90 and WriterAgentCapability.cs:22-28
**Issue:** AgentOutput has InputTokens, OutputTokens, CacheReadTokens, and CacheCreationTokens properties, but neither BuildOutput override sets them. The ChatResponse from Microsoft.Extensions.AI exposes usage information via response.Usage. These token counts are important for cost tracking and the Analytics capability itself.

Fix -- pass ChatResponse to BuildOutput and extract usage:

    protected virtual Result<AgentOutput> BuildOutput(
        string responseText, ChatResponse response, AgentContext context)
    {
        return Result<AgentOutput>.Success(new AgentOutput
        {
            GeneratedText = responseText,
            CreatesContent = CreatesContent,
            InputTokens = (int)(response.Usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(response.Usage?.OutputTokenCount ?? 0)
        });
    }

This is a breaking signature change for WriterAgentCapability.BuildOutput, but the surface area is small and controlled.

---

### W-03: Plan specifies agentic loop for Writer, implementation is single-call

**File:** WriterAgentCapability.cs
**Issue:** The section plan explicitly states Writer has High complexity with an agentic loop pattern -- tool calls for outline generation and section expansion, looping until final content. The implementation is a simple single-call: render prompt, get response, extract title. This is a significant deviation from the plan.

**Recommendation:** Either:
1. Implement the agentic loop as planned (likely a future iteration concern), or
2. Update the section plan to reflect the current simplified implementation as an intentional first pass, with a TODO linking to a follow-up task for the agentic loop.

As-is, merging the plan and implementation creates a documentation mismatch that will confuse future contributors.

---

### W-04: Plan calls for structured JSON parsing in Social and Repurpose, not implemented

**File:** SocialAgentCapability.cs, RepurposeAgentCapability.cs
**Issue:** The plan states:
- Social: Parse structured output -- JSON with text, hashtags, optional suggestedMedia
- Repurpose: Multi-output -- parse JSON array for blog-to-social transforms, each item in AgentOutput.Items

Neither capability implements JSON parsing or populates AgentOutput.Items or AgentOutput.Metadata. Both just return the raw responseText from the base class. This means downstream consumers expecting structured data will get raw text.

**Recommendation:** Same as W-03 -- either implement or update the plan. If deferred, at minimum add BuildOutput overrides that populate Metadata with at least a format=raw marker so consumers know the output is not yet structured.

---

### W-05: WriterAgentCapabilityTests duplicates BrandProfile creation instead of using TestBrandProfile

**File:** WriterAgentCapabilityTests.cs:45-56
**Issue:** WriterAgentCapabilityTests has its own CreateBrandProfile() method, while the other four test classes use the shared TestBrandProfile.Create() helper. This is duplicated code that will drift if the model changes.

Fix -- use shared helper and remove the private CreateBrandProfile() method:

    BrandProfile = TestBrandProfile.Create(),  // instead of CreateBrandProfile()

---

### W-06: No test for exception handling path

**Issue:** The base class has a catch-all Exception block that returns Result.Failure, but no test across any of the five test files validates this path. This is core error handling logic that should be covered.

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenChatClientThrows()
    {
        SetupPrompts("writer", "blog-post");
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API unavailable"));

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
    }

---

### W-07: No test for template render failure

**Issue:** If IPromptTemplateService.RenderAsync throws (missing template, Liquid syntax error), the same catch block handles it. But this is a distinctly different failure mode that deserves its own test -- especially since template names are derived from user-supplied parameters via context.Parameters.

---

## Suggestions (Consider Improving)

### S-01: Parameters dictionary allows arbitrary template names without validation

**File:** AgentCapabilityBase.cs:35
**Issue:** context.Parameters.GetValueOrDefault takes any string from parameters as a template name. While IPromptTemplateService will likely throw if the template does not exist, this is an indirect failure. Consider validating against IPromptTemplateService.ListTemplates() or at minimum documenting the valid template names per capability.

---

### S-02: Consider making BuildVariables add parameters under a task key

**File:** AgentCapabilityBase.cs:67-80
**Issue:** The plan states variables should include a task key containing AgentTask.Parameters. The implementation instead spreads parameters directly into the variables dictionary root via the foreach loop. This means a parameter named brand or content would silently overwrite the brand profile or content model.

Current -- parameters can collide with reserved keys:

    foreach (var param in context.Parameters)
        variables[param.Key] = param.Value;

Safer -- namespace parameters under task:

    variables["task"] = context.Parameters;

This also aligns with how Liquid templates would typically reference parameters: task.targetWordCount vs targetWordCount at root level.

---

### S-03: Base class could benefit from CancellationToken awareness for RenderAsync

**File:** AgentCapabilityBase.cs:38-39
**Issue:** IPromptTemplateService.RenderAsync does not accept a CancellationToken, but the two render calls happen before the chat client call. If the operation is cancelled between renders, there is no early exit. This is minor since rendering is fast, but worth noting if the interface is still being finalized.

---

### S-04: Consider adding a base class test fixture

**Issue:** All five test classes duplicate the SetupPrompts and SetupChatResponse helper methods identically. Consider extracting an AgentCapabilityTestBase class with these helpers and CreateContext scaffolding.

---

### S-05: Title regex is narrow -- only matches H1 format

**File:** WriterAgentCapability.cs:36
**Issue:** The regex only matches Markdown H1 headers (lines starting with # followed by text). If the LLM returns a title in other formats (e.g., Title: My Post, HTML h1 tags, or YAML frontmatter), it will return null. This is acceptable for now if prompts are structured to enforce H1 output, but worth documenting the assumption.

---

## Test Coverage Assessment

| Capability | Tests | Coverage Gaps |
|-----------|-------|---------------|
| Writer | 7 tests | Missing: exception path, RenderAsync failure, title-not-found case explicitly tested |
| Social | 5 tests | Missing: exception path, JSON parsing (per plan) |
| Repurpose | 4 tests | Missing: exception path, multi-output Items (per plan) |
| Engagement | 4 tests | Missing: exception path |
| Analytics | 4 tests | Missing: exception path |

**Total tests:** 24
**Missing per plan:** Writer Advanced tier for articles over 2000 words test, Repurpose multiple output items test, Social Standard tier for threads test.

Overall coverage is reasonable for the happy path but lacks negative/error path testing. Adding the exception handling test (W-06) and template validation failure test (W-07) would significantly improve confidence.

---

## Code Quality Summary

**Strengths:**
- Clean template-method pattern via AgentCapabilityBase avoids duplication across 5 capabilities
- Proper use of sealed on concrete capabilities
- GeneratedRegex source generator for title extraction is a performance best practice
- All capabilities correctly implement IAgentCapability interface
- Consistent use of Result pattern -- no exceptions thrown to callers
- File sizes well within guidelines (largest is 39 lines)
- Good separation of concerns -- capabilities do not create domain entities

**Areas for improvement:**
- Significant deviation from plan (agentic loop, JSON parsing, Items) needs to be reconciled
- Token usage data is lost, which impacts cost tracking
- Test duplication across fixtures

---

## Verdict

**WARNING** -- The implementation is sound as a simplified first pass, but has meaningful deviations from the section plan (W-03, W-04) and missing functionality (W-02 token counts). No security or correctness blockers.

**Must address before merge:**
- W-01: Stop leaking ex.Message to callers
- W-05: Remove duplicated CreateBrandProfile in Writer tests
- W-06: Add at least one exception-path test

**Should address before merge or immediately after:**
- W-02: Populate token counts from ChatResponse.Usage
- W-03/W-04: Update section plan to reflect simplified implementation, or implement planned features
- S-02: Namespace parameters under task key to prevent variable collisions
