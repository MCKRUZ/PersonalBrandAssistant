# section-10-frontend-editor

**Frontend section. Test runner: `ng test` (Jasmine/Karma), run from `src/PersonalBrandAssistant.Web/`.**

## Goal

Add an Angular **Brand Profile editor** page so the owner can edit the ranking profile (positioning, audience, recency/decay knobs, anti/authority multipliers, and per-pillar **weights + definitions**) that drives the feed composite rank. The editor has two distinct save paths that mirror the server's two write modes:

- **Weight / knob changes auto-apply** — a *weights-only* PUT that does **not** bump the profile version and triggers **no** LLM re-score. After a successful PUT the editor tells the Ideas list to reload (re-rank is server-side at query time, so it is instant and free).
- **Pillar-definition changes** (pillar name/description, authority topics, anti topics, voice markers, add/remove pillar) are **staged** and require an explicit **"Save & re-score" confirm dialog** that warns about LLM cost before the PUT goes out.

This section is **frontend only**. It consumes the API from section-09.

## CRITICAL — binding cross-section note

The backend ranking aggregate is `BrandRankingProfile` (the unrelated voice `BrandProfile` is left alone). **Section-09 shipped the API at `GET`/`PUT /api/brand-ranking-profile`** with `BrandRankingProfileDto` (fields: `id`, `version`, `positioning`, `audiencePrimary`, `audienceSecondary`, `halfLifeDays`, `decayFloor`, `antiTopicMultiplier`, `authorityBoost`, `pillars[]`, `authorityTopics[]`, `antiTopics[]`, `voiceMarkers[]`, `updatedAt`, `concurrencyToken`). Use that route and those field names — do **not** hardcode `/api/brand-profile`. Confirm against the shipped `BrandRankingProfileEndpoints.cs`/DTO before finalizing.

## Background the implementer needs

### Ranking profile shape (what the editor edits)
Per section-09's DTO above. `version` is display-only/read-only. `pillars`: `{ id, name, description, weight (0..1), order }`. `concurrencyToken` must be round-tripped on every PUT so a stale write is rejected (`409`).

### Two server-enforced write modes (R-H4)
The server distinguishes weights-only vs definition updates and cannot be tricked. The frontend's job is only to route the user's *intent* correctly:
- Slider/knob edits → call `updateBrandProfile` in **weights-only** mode. After success, reload the Ideas list.
- Definition edits → stage locally, then on confirm call `updateBrandProfile` in **definition** mode. After success, surface a brief "re-score queued" notice.

Match section-09's actual update-request contract (explicit `mode` discriminator vs. inferred from changed fields). Regardless, the UI still routes weight edits through auto-apply and definition edits through the confirm dialog — that is the UX contract.

### Existing frontend patterns (do not reinvent)
- Service: `src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts` — `@Injectable({ providedIn: 'root' })`, `baseUrl = '/api'`, `HttpClient`. Add the brand-profile methods here.
- Models: `src/.../app/models/` — add `brand-profile.model.ts` (new file, distinct domain).
- Store: `src/.../features/ideas/store/idea.store.ts` — NgRx signal store. `loadIdeas` is the reload trigger.
- Routes: `src/.../features/ideas/ideas.routes.ts` — lazy `loadComponent` standalone routes.
- Component style: standalone, `inject()`, PrimeNG modules, `data-testid` selectors. See `view-toggle.component.ts`.
- Spec style: `TestBed`, `provideHttpClient()` + `provideHttpClientTesting()`, `httpMock.expectOne()`, `req.flush()`, `afterEach(() => httpMock.verify())`.

## Files to create / modify

**Modify:**
- `src/.../core/services/idea.service.ts` — add `getBrandProfile()` / `updateBrandProfile(dto)`.
- `src/.../core/services/idea.service.spec.ts` — extend with brand-profile tests (**create the spec — no spec file exists today**).
- `src/.../features/ideas/ideas.routes.ts` — add the `brand-profile` route.

**Create:**
- `src/.../app/models/brand-profile.model.ts`
- `src/.../features/ideas/pages/brand-profile/brand-profile.component.ts`
- `src/.../features/ideas/pages/brand-profile/brand-profile.component.spec.ts`

> The store's `viewMode: 'ranked'` change + view-toggle third button belong to **section-11**, not here. The only store interaction here is calling a reload after a weights-only apply. If no public reload method exists, add a tiny `reload()` to the store that calls the existing `loadIdeas` rxMethod — do not touch `viewMode`.

## Tests FIRST

Jasmine/Karma, `provideHttpClient()` + `provideHttpClientTesting()`, `afterEach(() => httpMock.verify())`. Use section-09's real route (`/api/brand-ranking-profile`).

### `idea.service.spec.ts` (create + extend)
```
# Test: getBrandProfile() GETs /api/brand-ranking-profile and returns the mapped DTO
# Test: updateBrandProfile(dto) PUTs /api/brand-ranking-profile with the request body (incl. concurrency token)
# (default-sort test for list() lives with section-11; do not duplicate here)
```

### `brand-profile.component.spec.ts` (new)
```
# Test: weight slider change auto-applies — calls updateBrandProfile in weights-only mode AND reloads the Ideas list, with NO confirm dialog shown
# Test: a pillar name/description edit is STAGED — it does NOT PUT immediately; requires the "Save & re-score" confirm before any PUT
# Test: the confirm dialog WARNS about LLM cost before a definition save is sent
# Test: reactive form validation blocks save when a weight is outside [0,1], or there are zero pillars, or positioning/audience is empty
# Test: on load, GETs the active profile and populates the reactive form
# Test: a 409 conflict on save surfaces a "profile changed elsewhere, reload" message (does not silently lose the edit)
```

Spy on `IdeaService.getBrandProfile`/`updateBrandProfile` (`of(...)` / `throwError`) and on the store reload. For the confirm-dialog test, spy on PrimeNG `ConfirmationService` and assert the accept callback triggers the PUT, reject does not. Use `data-testid` selectors.

## Implementation detail

### `brand-profile.model.ts`
```ts
export interface BrandPillar {
  id: string; name: string; description: string; weight: number; order: number;
}
export interface BrandRankingProfile {
  id: string; version: number;
  positioning: string; audiencePrimary: string; audienceSecondary: string | null;
  halfLifeDays: number; decayFloor: number; antiTopicMultiplier: number; authorityBoost: number;
  pillars: BrandPillar[];
  authorityTopics: string[]; antiTopics: string[]; voiceMarkers: string[];
  concurrencyToken: string; updatedAt: string;
}
export interface UpdateBrandProfileRequest { /* fields per section-09 contract */ }
```

### `idea.service.ts` additions
```ts
private readonly brandProfileUrl = `${this.baseUrl}/brand-ranking-profile`;

getBrandProfile(): Observable<BrandRankingProfile> {
  return this.http.get<BrandRankingProfile>(this.brandProfileUrl);
}
updateBrandProfile(request: UpdateBrandProfileRequest): Observable<BrandRankingProfile> {
  return this.http.put<BrandRankingProfile>(this.brandProfileUrl, request);
}
```
(If section-09's PUT returns 204, type as `Observable<void>` and re-GET after a definition save to refresh `version`/`concurrencyToken`.)

### `ideas.routes.ts` addition
```ts
{
  path: 'brand-profile',
  loadComponent: () => import('./pages/brand-profile/brand-profile.component')
    .then((m) => m.BrandProfileComponent),
},
```

### `brand-profile.component.ts`
Standalone. Inject `FormBuilder`, `IdeaService`, `IdeaStore`, `ConfirmationService` (+ `ConfirmDialogModule`). Import `ReactiveFormsModule`, `SliderModule`, `InputTextModule`/textarea, `ChipsModule`, `ButtonModule`, `ConfirmDialogModule`.

Separate the form into two groups:
- **knobs/weights**: `halfLifeDays`, `decayFloor`, `antiTopicMultiplier`, `authorityBoost`, each pillar's `weight` → weights-only.
- **definitions**: `positioning`, `audiencePrimary`, `audienceSecondary`, each pillar `name`/`description`/`order`, add/remove pillar, `authorityTopics`, `antiTopics`, `voiceMarkers` → definition edits.

Behavior:
1. **On init** — `getBrandProfile()`, patch the form, stash `concurrencyToken` + `version` (version read-only).
2. **Weight/knob change** — subscribe to those controls' `valueChanges` (debounce ~300–500 ms). On settled change build a weights-only request (carrying `concurrencyToken`), call `updateBrandProfile`; on success update the stashed token + call store reload. **No confirm dialog.** On 409, show "profile changed elsewhere — reload"; do not blind-retry.
3. **Definition change** — staged (dirty), no auto-PUT. A "Save & re-score" button (enabled when definitions group dirty) opens a confirm dialog whose message warns about LLM token cost. On accept: definition update request (with token) → success → refresh token/version + "re-score queued" notice. On reject: do nothing.
4. **Validation** — each pillar `weight` in [0,1]; at least one pillar; `positioning`/`audiencePrimary` non-empty; `halfLifeDays > 0`; `decayFloor` in [0,1]; `antiTopicMultiplier > 0`; `authorityBoost > 0`. Disable save when invalid. (Weights need not be force-normalized client-side; server renormalizes per R-C2b — may show the live sum as a hint.)
5. **Concurrency** — every update includes the last-known `concurrencyToken`. 409 → surface, do not overwrite.

`data-testid`: `pillar-weight-{i}`, `pillar-name-{i}`, `pillar-desc-{i}`, `save-rescore`, confirm dialog accept/reject.

## Dependencies

- **section-09-brand-profile-api** (REQUIRED, blocks this section) — route `/api/brand-ranking-profile`, DTO shape, two-write-mode contract, concurrency token.
- **section-11-frontend-ranked-view** — owns the `viewMode: 'ranked'` store change + third toggle button. This section only triggers an Ideas-list reload; it must not modify `viewMode`.

## Verify

From `src/PersonalBrandAssistant.Web/`:
- `ng test --watch=false --browsers=ChromeHeadless` — all new/extended specs green.
- `ng build` — the lazy route resolves.
- Manual smoke: navigate to `brand-profile`, move a weight slider → Ideas list reloads with no dialog; edit a pillar description → "Save & re-score" enables → clicking shows the LLM-cost warning before saving.

## As built (2026-06-16)

- **Standalone `BrandProfileComponent`** at `features/ideas/pages/brand-profile/`, route `brand-profile`.
  `idea.service.ts` gained `getBrandProfile()`/`updateBrandProfile()` against `/api/brand-ranking-profile`.
  New `brand-profile.model.ts`. No store change — `loadIdeas` is already public (section-11 owns `viewMode`).
- **No-smuggle property (review-hardened):** the auto-apply is gated on `!definitionsDiffer()`. Weight/knob
  edits auto-apply as a weights-only PUT (built from BASELINE definitions) and reload the Ideas list — but
  ONLY when no definition diff is staged. The moment a definition edit (text, **add/remove/reorder pillar**)
  is pending, every change routes through the "Save & re-score" confirm (warns about LLM cost). `order` is a
  definition field; `weight` is the only weights-only pillar field.
- **Concurrency:** token round-tripped on every PUT; a definition-path 409 surfaces "changed elsewhere" and
  preserves the staged edit; a weights-path 409 resyncs from the server (no blind-retry). A weights apply
  that unexpectedly bumps the version surfaces a "re-scored" notice (defense in depth).
- **Validation:** min-1 pillar, weight [0,1], positioning/audience non-empty, halfLife > 0, decayFloor
  [0,1], positive multipliers; no weights-sum rule (server renormalizes). Form hidden until the profile
  loads; a11y labels on the slider + add/remove buttons.
- **Tests:** `idea.service.spec.ts` (+2), `brand-profile.component.spec.ts` (13, incl. gating, confirm,
  both 409 paths, add/remove/zero-pillar). `ng build` clean; `ng test` 570 green.
