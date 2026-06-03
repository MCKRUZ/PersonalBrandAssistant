# TDD Companion — Content Studio Redesign

Test-first stubs mirroring `claude-plan.md`. Conventions (from `claude-research.md`): Jasmine/Karma,
colocated `*.spec.ts`, run with `ng test` (`ng test --watch=false --browsers=ChromeHeadless`).
Mock only HTTP (`HttpTestingController`) and SignalR; use real signalStores. 80% coverage on new code.
Stubs are descriptions, not implementations — write the failing test, then the code.

Priority: the **pure-logic** units (display utils, markdown adapters, thread splitter, transition map,
delivery badge) are the highest-value, DOM-free tests — write these first in each section.

---

## Section 1 — Styling foundation
Mostly non-logic (SCSS/tokens). Guard rails rather than unit tests:
```
# Test (build): `ng build` succeeds after wiring partials + tokens.
# Test (sanity, optional): a fixture element with `background: var(--surface-card)` computes to #141418
#   (jsdom/karma getComputedStyle) — proves :root tokens resolve.
# Manual/lint check: no GitHub-dark hexes (#0d1117/#161b22/#30363d/#58a6ff/#1f6feb) remain under
#   features/content or shell/sidebar (grep gate in review, not a unit test).
```

## Section 2 — Shared atoms & store extensions

### 2.1 content-display.utils.spec.ts (pure — write first)
```
# Test: STATUS_META has an entry for every ContentStatus; colors are css var refs; order is 0..n unique.
# Test: TYPE_GLYPH has an entry for every ContentType.
# Test: voiceBandColor(85)->high, voiceBandColor(80)->high (>=80 boundary, NOT >80),
#       voiceBandColor(60)->mid, voiceBandColor(59)->low, voiceBandColor(null)->empty/neutral.
# Test: relativeTime — past iso -> "2h"/"3d"; future iso -> "in 3d"; now -> "just now"/"0m".
# Test: nextStatus(Idea)->Draft, Draft->Review (or first legal), Approved->Scheduled,
#       Published->null, Archived->null (no forward move).
# Test: LEGAL_TRANSITIONS — Idea:[Draft]; Draft includes Review and Approved; Review includes
#       Approved and Draft; Approved includes Scheduled and Published; Scheduled includes Approved
#       and Published; Published includes Approved; every target is reachable by a real endpoint.
```

### 2.2 atom components
```
# status-tag.spec: renders dot + label in the status color for each ContentStatus input.
# voice-score-ring.spec: score=85 shows "85" + high color; score=null shows dashed-empty ring;
#                        respects size input.
# platform-dot.spec: variant='tile' renders the 2-letter code; variant='dot' renders a colored dot;
#                     correct color per Platform.
```

### 2.3 content.store.spec.ts (extend existing — high value)
```
# Test: loadAll() populates allContents from the (mocked) service; loading toggles.
# Test: counts() returns correct per-status tallies from allContents.
# Test: filtered() applies activeStatus + search (title AND tags) + popover filters together.
# Test: byStatus() groups filtered() into columns keyed by ContentStatus.
# Test: setActiveStatus toggles (set, re-set same clears to null).
# Test: setSearch updates search; filtered() reflects it.
# Test: transition(id, legalTarget) calls the CORRECT ContentService endpoint for the
#       (current,target) pair (draft/approve/submitForReview/requestChanges/unschedule/publish/
#       unpublish/restore) — assert via spy per transition.
# Test: transition(id, illegalTarget) is a no-op (no service call) + surfaces a notice.
# Test: transition optimistically patches ONLY status on a NEW array (reference changes, updatedAt
#       unchanged client-side); on service success the affected record is reloaded.
# Test: transition rolls back the status patch when the service errors.
# Test: transition does NOT call schedule (Approved->Scheduled is caller-driven, needs a date).
```

## Section 3 — Content list flow

### 3.1 content-list.component.spec.ts (update existing)
```
# Test: subtitle shows allContents().length pieces.
# Test: viewMode switch renders board/grid/table; each reads from filtered().
# Test: "+ New Content" navigates to /content/new (with seed params when from an idea card).
# Test: shows inspire empty-state when allContents empty; filtered empty-state when filtered empty
#       but content exists.
# Test: search input debounces ~300ms before calling setSearch.
```

### 3.2 pipeline-bar.spec.ts
```
# Test: renders an "All {total}" pill + one pill per ContentStatus with its count from counts().
# Test: clicking a pill calls setActiveStatus(status); clicking the active pill clears it.
# Test: zero-count pills render at reduced opacity; selected pill takes the status color.
```

### 3.3 content-board.spec.ts (the tricky one)
```
# Test: renders one cdkDropList column per status from byStatus().
# Test: canDropInto(target) predicate returns true only when dragged card's status->target is legal.
# Test: onDrop with same container is a no-op (no transition call).
# Test: onDrop legal cross-column calls store.transition(cardId, targetStatus).
# Test: onDrop where target===Scheduled opens the schedule dialog (does NOT call transition/schedule
#       directly); on confirm calls ContentService.schedule(id,{scheduledAt}).
# Test: empty column renders the dashed "Drop here" target.
# (Drag simulation: dispatch CDK drop with a synthesized CdkDragDrop object; assert the handler,
#  not the native drag gesture.)
```

### 3.4 content-card.spec.ts (update)
```
# Test: board variant shows type glyph + uppercase type label + voice ring + title + tag chips +
#       platform dots + relativeTime(updatedAt); scheduled shows "in {n}{unit}".
```

### 3.5 content-list-table.spec.ts
```
# Test: renders Status/Title/Type/Platforms/Voice/Updated columns; NO Actions column.
# Test: row click emits/open the detail drawer with that content's id.
```

### 3.6 detail-drawer.spec.ts
```
# Test: header shows status tag + serif title; meta list shows voice/platforms/updated|scheduled/tags.
# Test: footer "Open in editor" navigates to /content/:id.
# Test: footer shows "Publish ->" when status in {Approved,Scheduled}; else "Move to {nextStatus} ->".
# Test: "Move to {next}" calls store.transition(id, nextStatus); when next is Scheduled it opens the
#       schedule dialog instead.
```

### 3.7 filters-popover.spec.ts
```
# Test: platform/type/date controls update store filters; clearing resets them.
```

### 3.8 studio-empty-state.spec.ts
```
# Test: inspire variant renders idea-suggestion cards; clicking one navigates to /content/new with
#       seed query params (topic/type).
# Test: filtered variant renders "Nothing matches that filter" + "Clear filters" which resets
#       activeStatus/search/filters.
```

## Section 4 — Editor flow

### 4.1 content-editor.component.spec.ts (update — remove splitter assertions)
```
# Test: no p-splitter / app-markdown-editor in the template; manuscript-surface present.
# Test: ngOnInit on /content/new reads topic/type/sourceIdeaId query params into create().
# Test: autosave still fires only for Idea/Draft/Review (scheduleAutoSave) and not Approved+.
# Test: Assistant toggle shows/hides the side panel.
```

### 4.2 prose-editor.spec.ts (highest-risk — write round-trip test FIRST)
```
# Test (round-trip, gates the section): for h1-h3, bold, italic, links, bullet+ordered lists,
#       inline code — markdown -> setContent -> serialize -> markdown is stable (string-equal modulo
#       normalized whitespace). If a mark fails, it's excluded from the supported set + documented.
# Test: valueChange emits debounced serialized markdown on edit.
# Test: setContent is NOT re-applied when incoming value equals last serialized output (no caret reset)
#       and is skipped while the editor is focused.
# Test: editable=false (readOnly) blocks edits.
# Test: paste of HTML is sanitized (no script/style; allowlisted marks only).
```

### 4.3 manuscript-surface.spec.ts
```
# Test: title edit calls store.updateField('title', …); body edit calls updateField('body', md) +
#       triggers scheduleAutoSave.
# Test: status===Idea renders the dashed "still just an idea" panel + Start draft (calls onStartDraft);
#       Draft+ renders the prose editor.
# Test: derived subtitle is display-only (never sent to updateField/UpdateContentRequest).
```

### 4.4 stage-tracker.spec.ts (pure)
```
# Test: active dot index per status — Idea0/Draft1/Review2/Approved3/Scheduled4/Published5.
# Test: Archived renders all-muted terminal state (no active dot).
# Test: dots before current are filled; after current are empty.
```

### 4.5 editor-top-bar.spec.ts
```
# Test: back navigates to /content; stage-tracker receives status; saved indicator reflects
#       isSaving()/isDirty() (Saving/Unsaved/Saved); voice ring shows score; Assistant toggle emits.
```

### 4.6 voice-meter.spec.ts
```
# Test: value + color by band (>=80/>=60/else) from voiceScore; band note text per band.
# Test: re-check action calls ContentService.voiceCheck(id) and updates the displayed score/feedback.
```

### 4.7 sidecar-chat.spec.ts (update)
```
# Test: assistant vs user bubble styling; 3-dot thinking shown while isStreaming.
# Test: quick-action chips differ for empty body vs has-body.
# Test: Apply writes via applyToEditor; Copy puts text on the clipboard.
# Test: SignalR wiring unchanged — sendChatMessage called; tokens$ appended; complete clears stream.
```

### 4.8 platform-targets.spec.ts (update)
```
# Test: selecting/deselecting updates targetPlatforms (logic unchanged); restyle only.
```

## Section 5 — Publish overlay flow

### 5.1 platform-metadata.spec.ts (pure — write first)
```
# Test: PLATFORM_META has an entry per PUBLISHABLE_PLATFORMS with delivery/charLimit/fmt/code/label;
#       Blog auto/null, Medium manual/null, Substack auto/null, LinkedIn auto/3000, Twitter auto/280.
# Test: deliveryBadge(meta, connected) -> "⚡ Auto-publish" variant auto;
#       deliveryBadge(autoMeta, !connected) -> "⚡ Connect to auto-publish" variant warn;
#       deliveryBadge(manualMeta, *) -> "✋ Manual" variant manual.
```

### 5.2 markdown-blocks.spec.ts (pure — write first)
```
# Test: toBlocks maps "# H"->h1, "## H"->h2, "### H"->h3, paragraphs->p, in document order.
# Test: plainText strips markdown — "**bold**"->"bold", "[label](url)"->"label" (href excluded),
#       "`code`"->"code", headings/lists contribute their text only.
# Test: plainText length is the RENDERED length (a 290-char paragraph with **markers** still
#       reports the visible char count, so budgets are correct).
```

### 5.3 thread-splitter.spec.ts (pure — write first; boundary-critical)
```
# Test: short text -> single tweet, no numbering when 1 tweet (or "1/1" per chosen rule).
# Test: long text -> multiple tweets, each (including the "i/n" suffix) <= 280.
# Test: boundary — a length that would be 280 WITHOUT the suffix splits so the numbered tweet stays
#       <= 280 (suffix budget reserved).
# Test: multi-digit n (e.g. 12 tweets) still keeps every numbered tweet <= 280.
# Test: splits on sentence boundaries when possible (greedy packing).
```

### 5.4 publish-modal.component.spec.ts
```
# Test: destinations list one row per PUBLISHABLE_PLATFORMS; primary checked+disabled.
# Test: delivery badge + char/thread usage per row driven by plainText(body) and PLATFORM_META.
# Test: selecting destinations updates preview tabs + footer summary "{n} dest · {a} auto · {m} manual".
# Test: "Publish {n}" disabled when none selected, or schedule mode with no datetime.
# Test: schedule mode disables the destinations list (shows the note) — selection ignored.
# Test: confirm in now-mode calls publish(id,{targetPlatforms:selected}); schedule-mode calls
#       schedule(id,{scheduledAt}) with NO platforms.
# Test: a11y — focus trapped within modal; Esc closes; scrim click closes; aria-modal set.
```

### 5.5 previews/*.spec.ts (×5)
```
# blog-preview: renders kicker/serif H1/lede/byline/H2 sections from blocks.
# medium-preview: bold H1 + gray subtitle + author/Follow + clap-bookmark bar.
# substack-preview: masthead + Subscribe + "to N subscribers" + unsubscribe footer.
# linkedin-preview: plainText truncated ~210 chars + "…more"; over-3000 shows a warning.
# twitter-preview: renders splitThread(plainText,280) as numbered 1/n tweets on the connector rail.
```

### 5.6 delivery-badge.spec.ts + publish-result.spec.ts
```
# delivery-badge: pill text/variant matches deliveryBadge() for the auto/connected/manual matrix.
# publish-result: auto+connected row -> Publishing -> Published(View) as getPublishStatus resolves;
#                 manual row -> Ready to post + Copy (clipboard gets platform-formatted body) + Open.
# publish-result: schedule mode row -> "◴ Scheduled for {datetime}" (frontend-only, not PublishStatus).
# publish-result: polling stops when all platformStatuses in {Published,Failed}; interval cleared on
#                 destroy; cap ~30s -> "still processing".
```

## Coverage targets
80% on all new files. The pure-logic modules (display utils, platform-metadata, markdown-blocks,
thread-splitter, store transition/computeds) should approach 100% — they carry the business rules and
are cheap to cover. Component specs cover render-by-state + the key interactions listed above.
