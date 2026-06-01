# Section 13 Code Review: Frontend Connections

## Verdict: WARNING - 3 HIGH, 7 MEDIUM, 7 LOW

## HIGH

### HIGH-1: Unsanitized query param in notification message
platform-connections.component.ts - `params['connected']` from URL interpolated into notification. Validate against known platform list.

### HIGH-2: Subscription leak on queryParams
platform-connections.component.ts - `route.queryParams.subscribe()` never unsubscribed. Need `takeUntilDestroyed(destroyRef)`.

### HIGH-3: Uses @Input/@Output decorators instead of signal inputs
platform-card.component.ts, medium-token-form.component.ts, substack-login-form.component.ts - Codebase standardized on `input()` / `output()` signal APIs.

## MEDIUM

### MEDIUM-1: No platform parameter type validation in service
platform-connection.service.ts - Raw string for platform param, should use union type.

### MEDIUM-2: getStatus() called per template change detection
platform-connections.component.ts - `[status]="getStatus(config.platform)"` runs Array.find() every CD cycle.

### MEDIUM-3: window.location.href untestable
platform-connections.component.ts - Direct window assignment. Use DOCUMENT token.

### MEDIUM-4: credentialErrors mutated via delete
platform-connections.component.ts - `delete this.credentialErrors[event.platform]` mutates in place.

### MEDIUM-5: Disconnect error silently swallowed
platform-connections.component.ts - No user notification on disconnect failure.

### MEDIUM-6: Missing tests for credential form components
medium-token-form and substack-login-form have no spec files.

### MEDIUM-7: Duplicated CSS across form components
~20 lines identical CSS in both credential forms.

## LOW

LOW-1: loadPlatforms() called twice on OAuth return
LOW-2: Tab navigation lacks ARIA attributes
LOW-3: Form inputs use placeholder only, no label/aria-label
LOW-4: Empty .tab-content CSS rule
LOW-5: Non-null assertion on required input
LOW-6: Notification auto-dismiss missing
LOW-7: Service constructor injection consistent with codebase
