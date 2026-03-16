# Section 11 Code Review: DI Configuration and Final Wiring

**Reviewer:** code-reviewer agent
**Date:** 2026-03-14
**Verdict:** Approve with warnings

---

## CRITICAL Issues

### [CRITICAL] LogPromptContent defaults to true in production configuration

**File:** src/PersonalBrandAssistant.Api/appsettings.json:49
**File:** src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs:15

The LogPromptContent flag defaults to true both in the options class and in appsettings.json. If this flag controls whether full prompt text (including user content, brand context, potentially PII) gets written to logs, this is a data leakage risk in production. Prompts may contain personal brand details, audience data, or user-provided content that should not appear in production log sinks.

**Fix:** Default to false in the options class. Set to true only in appsettings.Development.json.

In AgentOrchestrationOptions.cs:

    public bool LogPromptContent { get; init; } = false;

In appsettings.Development.json (not the base appsettings.json):

    "AgentOrchestration": { "LogPromptContent": true }

Remove LogPromptContent from appsettings.json entirely, or set it to false.

---

## HIGH Issues

### [HIGH] IPromptTemplateService registration bypasses the Options pattern

**File:** src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs:51-59

The PromptTemplateService is registered as a singleton using a manual factory lambda that reads configuration directly via configuration.GetSection(...).Get<>(). This bypasses IOptions/IOptionsMonitor, meaning:

1. The PromptsPath value is captured once at startup and will not reflect any configuration reload.
2. The registration is inconsistent with the rest of the agent orchestration services, which use services.Configure<AgentOrchestrationOptions>(...).
3. The manual resolution of IHostEnvironment and ILogger with fully qualified type names is verbose and fragile.

**Fix (recommended):** Refactor PromptTemplateService to accept IOptions<AgentOrchestrationOptions> in its constructor so it participates in the standard options pattern. This also lets it react to options reloads if IOptionsMonitor is used later.

If the constructor signature cannot change now, at minimum use sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>() in the factory lambda so the value comes from the already-configured options pipeline:

    services.AddSingleton<IPromptTemplateService>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>().Value;
        return new PromptTemplateService(
            options.PromptsPath,
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<ILogger<PromptTemplateService>>());
    });

### [HIGH] IChatClientFactory is singleton but ITokenTracker is scoped -- verify lifetime safety

**File:** src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs:50, 60

ChatClientFactory is registered as singleton, and it wraps chat clients in TokenTrackingDecorator. Looking at ChatClientFactory.cs line 74, the decorator receives IServiceScopeFactory to create scopes per call, which is the correct pattern for accessing scoped services from a singleton.

This is technically correct, but worth flagging: verify that TokenTrackingDecorator creates and disposes a scope for each call to RecordUsageAsync. If it captures a scoped ITokenTracker once, it will use a disposed DbContext on subsequent requests.

**Status:** Likely correct based on the IServiceScopeFactory parameter, but confirm the decorator implementation.

### [HIGH] DI test creates a scope but never disposes it

**File:** tests/.../DependencyInjection/AgentServiceRegistrationTests.cs:248

    return factory.Services.CreateScope().ServiceProvider;

The IServiceScope is never disposed. This leaks scoped services (including DbContext) for the lifetime of the test. While unlikely to cause test failures, it sets a bad pattern.

**Fix:** Store the scope and dispose it, or have the test class implement IDisposable:

    private IServiceScope? _scope;

    private IServiceProvider CreateServiceProvider()
    {
        var factory = _factory.WithWebHostBuilder(builder => { ... });
        _scope = factory.Services.CreateScope();
        return _scope.ServiceProvider;
    }

    public void Dispose() => _scope?.Dispose();

---

## MEDIUM Issues

### [MEDIUM] DefaultModelTier is a string instead of the ModelTier enum

**File:** src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs:9

    public string DefaultModelTier { get; init; } = "Standard";

Using a string here means invalid values (typos, casing issues) will only be caught at runtime when something tries to parse it. The Models dictionary also uses string keys instead of ModelTier.

**Fix:** Use the enum type. ASP.NET configuration binding supports enum values:

    public ModelTier DefaultModelTier { get; init; } = ModelTier.Standard;
    public Dictionary<ModelTier, string> Models { get; init; } = new();

### [MEDIUM] ApiKey field present in appsettings.json (even if empty)

**File:** src/PersonalBrandAssistant.Api/appsettings.json:59

    "ApiKey": "",

While the ChatClientFactory correctly throws if the API key is missing, having an ApiKey field in appsettings.json (even empty) creates a risk: someone might paste a real key here and commit it. The field should not appear in the committed config file at all. The constructor already throws a clear error message directing users to User Secrets / Key Vault.

**Fix:** Remove the ApiKey field from appsettings.json. Document the setup in the README or a development guide:

    dotnet user-secrets set "AgentOrchestration:ApiKey" "your-key"

### [MEDIUM] MockChatClient._callCount read is not volatile

**File:** tests/.../Mocks/MockChatClient.cs:37

The _callCount field is incremented with Interlocked.Increment (correct), but the CallCount property reads it without Volatile.Read:

    // Current
    public int CallCount => _callCount;

    // Suggested
    public int CallCount => Volatile.Read(ref _callCount);

For test code this is unlikely to cause issues, but for consistency with the Interlocked writes.

### [MEDIUM] Test mocks refactored from static to instance -- confirmed correct

**File:** tests/.../Api/AgentEndpointsTests.cs:153-155

The mocks are now instance fields (good -- no longer static), and since xUnit constructs a new test class instance per test, each test gets fresh mock instances. The previous static + Reset() pattern was error-prone. This is a correct improvement. No action needed.

---

## LOW Issues / Suggestions

### [LOW] SSE error sanitization could use an allowlist approach

**File:** src/PersonalBrandAssistant.Api/Endpoints/AgentEndpoints.cs:72-78

The current approach only exposes detailed errors for ValidationFailed:

    var safeMessage = result.ErrorCode == ErrorCode.ValidationFailed
        ? string.Join("; ", result.Errors)
        : "Agent execution failed.";

Good improvement. Consider whether NotFound errors also have safe-to-expose messages. Could evolve into a helper method or a property on ErrorCode indicating whether messages are user-safe.

### [LOW] Capability registrations could use assembly scanning

**File:** src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs:62-66

Five explicit capability registrations will grow as new capabilities are added. Consider using Scrutor or a manual assembly scan to auto-register all IAgentCapability implementations. Not urgent with 5 registrations, but worth considering as the system grows.

### [LOW] Hardcoded capability count in test assertion

**File:** tests/.../DependencyInjection/AgentServiceRegistrationTests.cs:88

    Assert.Equal(5, capabilities.Count);

This magic number will break when a new capability is added. Consider using reflection to count IAgentCapability implementations in the assembly, or at minimum add a comment explaining why 5.

### [LOW] Unnecessary await Task.CompletedTask in MockChatClient

**File:** tests/.../Mocks/MockChatClient.cs:56

    await Task.CompletedTask;

This is unnecessary. The method can return Task.FromResult(...) instead. Minor style point for test code.

---

## Section-10 Leftover Fixes

The two changes carried over from section-10 are both sound:

1. **Removed unused writer variable** (AgentEndpoints.cs:34) -- clean.
2. **SSE error sanitization** (AgentEndpoints.cs:72-78) -- good security improvement. Only validation errors expose details; all others get a generic message.

---

## Summary

| Priority | Count | Status |
|----------|-------|--------|
| Critical | 1     | LogPromptContent default must be false for production safety |
| High     | 3     | Options pattern bypass, scope leak in tests, lifetime verification |
| Medium   | 3     | String enum, empty API key in config, thread safety |
| Low      | 4     | Minor improvements |

**Verdict: Approve with warnings.** The critical LogPromptContent default should be fixed before merge. The high issues are correctness-adjacent but not blocking. The DI wiring itself is sound -- lifetimes are appropriate, and the test refactoring from static to instance mocks is a good improvement.
