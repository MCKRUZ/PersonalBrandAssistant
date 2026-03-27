# Section 02 — Code Review Interview Transcript

## Review Triage

### Auto-Fixed (5 items)
1. **JsonDocument leak in ReceiveFrameAsync** — Added `using var doc` and `.Clone()` on the payload element to prevent pooled memory from becoming invalid after GC.
2. **No read-side thread safety** — Added `_activeStream` guard with `Interlocked.CompareExchange` to enforce single-consumer semantics. `SendTaskAsync` now throws if called concurrently.
3. **ConnectAsync disposing active socket** — Added guard check: throws `InvalidOperationException` if `_activeStream == 1`. Also moved `_isConnected = true` to after successful handshake.
4. **Unbounded message size** — Added `MaxMessageSize` constant (1 MB) and check in receive loop.
5. **_isConnected set before handshake** — Moved `_isConnected = true` to after session-update response is received.

### Let Go (5 items)
1. **SidecarOptions validation** — Deferred to section-12 (DI configuration) where validation attributes are added.
2. **Random port flakiness in tests** — Tests pass consistently; no flakiness observed.
3. **Graceful WebSocket close on dispose** — Not critical for this phase.
4. **Missing reconnection test** — Reconnection logic was descoped from this section per plan.
5. **Test for SendTaskAsync without ConnectAsync** — Nice-to-have, `InvalidOperationException` already thrown.

## Verification
- Build: **Pass** (0 warnings, 0 errors)
- Tests: **681 passed** (158 Domain + 106 Application + 417 Infrastructure)
