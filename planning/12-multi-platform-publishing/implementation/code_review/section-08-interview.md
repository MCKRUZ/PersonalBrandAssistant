# Section 08 Code Review Interview

## Auto-fixes Applied

### HIGH-2: Image upload advertised but not implemented
- Set `SupportsImages: false` in GetCapabilities
- Removed dead `LinkedInImageUploadResponse` and `LinkedInImageUploadValue` records
- Image upload is a complex 3-step flow; will implement when image data actually flows through the system

### HIGH-3: ValidateCredentialsAsync bypasses token refresh
- Changed to route through `GetValidTokenAsync` instead of raw `encryptor.Decrypt`
- Near-expired tokens now get refreshed during validation, matching publish behavior

### HIGH-4: No test for Draft/Schedule rejection
- Added `[Theory]` test with `[InlineData(PublishMode.Draft)]` and `[InlineData(PublishMode.Schedule)]`
- Asserts `result.Success == false` and error message contains "does not support"

### MEDIUM-1: Unused `capturedBodyHolder` parameter
- Removed from `SetupUserInfoAndPost()` signature

### MEDIUM-5: Unused `SetupHttpResponses` helper
- Removed dead test infrastructure method

## Let Go

### HIGH-1: Dead _options injection
- By design. `IOptionsMonitor<LinkedInOptions>` captured as `_options` field following codebase convention.
- Will be used when Enabled guard or config-driven API version is needed.
- Removing and re-adding later would be churn.

### MEDIUM-2: LinkedInVersion constant
- Hardcoded `202604` is acceptable. LinkedIn versions are supported for 12 months.
- Moving to config is premature — we'll update the constant when needed.

### MEDIUM-3/4: Test duplication and code block whitespace
- Acceptable test duplication. Each test is self-contained and readable.
- Code block whitespace artifacts handled by CollapseBlankLines regex.

### All LOW and NITPICK issues
- Accepted as-is. Minor improvements not worth the change.
