# Section-04 Review Triage & Decisions

No user interview required — all findings are correctness/robustness/coverage improvements with no product tradeoff. Decisions autonomous.

## Auto-fixed

### #1 (MEDIUM) — YouTube multi-page paging test
Added `PollAsync_PagesUploadsPlaylist_AcrossPageTokens_ToReachN`: page 1 returns 10 ids + a next-page token, page 2 returns 5 more + null; asserts N=12 reached AND that page 2 was fetched with the token page 1 returned. Proves `pageToken` advancement.

### #2 (MEDIUM) — Generalized the deep-analytics mapper (was `day`-hardcoded)
`YouTubeReportResult` now carries typed columns (`YouTubeReportColumn(Name, ColumnType)` where ColumnType is "DIMENSION"|"METRIC", populated from the SDK's `ResultTableColumnHeader.ColumnType`). The mapper labels points by the first DIMENSION column and makes one series per METRIC column — so traffic-source/geography/demographics variants (no `day` column) map correctly instead of producing a bogus all-zero series. Added `MapsNonDayDimension_LabelsByThatDimension`. Section-06 consumes this mapper for exactly those variants.

### #3 + #4 (MEDIUM / MED-LOW) — Real IG client-level tests
Added `InstagramGraphClientTests` exercising the actual seam (canned Graph JSON via mocked handler):
- `GetAccountMetricsAsync_MapsByReturnedName_DropsMetricsNotInResponse` — a requested-but-dropped metric ("saves") is simply absent; the poll doesn't fail. This is the plan's "critical" deprecation-tolerance behavior, now genuinely covered (the facade test alone was a trivial pass-through).
- `GetRecentMediaAsync_ParsesPerMediaValuesArray` — locks the `values[0].value` per-media shape (distinct from the account `total_value.value` shape). If the real media endpoint returns `total_value` instead, the smoke test will catch it; our parse of the documented shape is now verified.

### #5 (LOW) — Max-page guards
Both paging loops now cap iterations (YouTube 50 uploads pages, TikTok 50 video pages) so a misbehaving API (empty pages with has_more=true / non-null token, N never reached) can't loop unbounded on the daily poller.

### #6 (LOW) — Exact-key-count assertion
Added `Assert.Equal(3, video.Metrics.Count)` to the YT per-video test (matching the account test), guarding against stray keys.

### #7 (LOW, security) — IG token moved to the Authorization header
`InstagramGraphClient` now sends the token as `Authorization: Bearer` (like `TikTokDisplayClient`) instead of in the query string, avoiding proxy/log capture.

## Documented (untested-seam smoke-test items)
The SDK/HTTP client implementations (`YouTubeApiClient`, `InstagramGraphClient`, `TikTokDisplayClient`) remain untested seams by design. Before prod, a real-credential smoke test must confirm: the IG media-insights response shape (`values[]` vs `total_value`), the graph.instagram.com version-less path, TikTok's error-in-200-body behavior, and the YouTube SDK field mappings. Recorded in the section doc.
