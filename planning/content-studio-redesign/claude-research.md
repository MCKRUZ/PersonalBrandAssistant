# Research — Content Studio Redesign

Combines (A) codebase research + exact prototype CSS extraction, and (B) verified
version-specific web research. Feeds `claude-plan.md`.

---

## 0. Versions & hard constraints (reconciled)

From `package.json`:
- **Angular `^19.2.0`** (NOT 20 — important for CDK pin below)
- **PrimeNG `^20.4.0`**
- `@ngrx/signals ^21.1.0` (signalStore), `@ngrx/operators`
- **`ngx-markdown ^21.3.0`** — bundles `marked` as a dependency (so `marked` is likely
  already resolvable transitively; the plan should confirm and either import it directly or
  add `marked` explicitly).
- `@microsoft/signalr ^10.0.0`, `primeflex ^4.0.0`, `primeicons ^7.0.0`
- **`@angular/cdk` NOT installed** (no CDK imports anywhere in `src/`).

**CDK version pin (reconciled):** CDK major tracks the Angular major **exactly**. App is
Angular 19 → add **`@angular/cdk: ^19.2.0`** (latest 19.x = 19.2.19). Do NOT use ^20 (the
web research's "net adds" assumed Angular 20 — wrong for this repo).

**marked:** current standalone major is **marked 18** (ESM-only, Node ≥20). But ngx-markdown
21.3.0 pins its own marked range — use whatever marked version ngx-markdown already brings to
avoid a version clash, OR add an explicit compatible `marked` dep. Verify the resolved marked
version during the prereqs section before relying on a specific token API.

**Do NOT touch:** `signalr.service.ts` transport. Public surface to preserve:
`tokens$`, `generationComplete$`, `generationError$` (Observables) + `connect()`,
`disconnect()`, `sendChatMessage(contentId, message)`.

---

## A. Prototype CSS — exact values (source of truth: `prototype/Content Studio.html` `<style>`)

### Root CSS custom properties (the prototype's token block)
```
--base:#0e0e10  --card:#141418  --elev:#1a1a20  --hover:#22222a
--border:#2c2c36  --disabled:#3a3a46
--text:#f0f0f5  --text-2:#8a8a96  --text-3:#5a5a66
--accent:#c87156  --accent-soft:#c8715622   /* brand @ ~13% */
--headline:'DM Serif Display',Georgia,serif   --mono:'JetBrains Mono',monospace
--r:12px  --r-sm:8px
```
> Naming maps to the app's SCSS: `--accent`→`$brand-primary`, `--base/card/elev/hover`→
> `$surface-*`, `--text/-2/-3`→`$text-primary/secondary/muted`. The `:root` token block the
> app must author should expose these AND the missing extras (sidebar `#0b0b0d`, inset
> `#0c0c0e`, publish-canvas `#08080a`, status Idea `#8a7df0`, delivery badge pairs, full
> radius scale, fonts).

### Sidebar
- width **212px**; bg **#0b0b0d**; border-right 1px solid var(--border); padding `18px 0 14px`.
- Brand: DM Serif Display 24px, line-height 1, letter-spacing .3px, padding `6px 22px 22px`;
  accent span color var(--accent), italic.
- Nav: container padding `0 12px`, item gap 2px. Item padding `9px 12px`, icon+label gap 13px,
  idle color var(--text-2); hover bg var(--hover) + color var(--text), transition .15s ease.
  Active (spec): bg var(--accent-soft) + icon var(--accent) (prototype leaves active styling
  to be applied — use the README spec).
- Footer user block: 32px gradient avatar `135deg, brand→#9c5440`, name 13px/600,
  "Solo studio" 11px var(--text-3), top border (from README).

### Pipeline bar + status pills
- Container padding `18px 28px 14px`, gap 8px, flex-wrap.
- Pill: padding `7px 13px`, radius **99px**, bg var(--card), border 1px var(--border),
  13px/500 var(--text-2), transition .14s. Hover bg var(--hover)+var(--text).
  Selected `.on`: bg var(--elev), color var(--text), border var(--disabled).
  All-pill selected: border var(--accent).
- Dot: 8×8px circle, flex-shrink 0. Empty status: opacity .5. Mono count chip.

### Board column + card
- Column: width/flex-basis **286px**, bg **#0c0c0e**, border 1px var(--border), radius
  var(--r)=12px, max-height 100%, transition border-color/box-shadow .14s. Header padding
  `14px 14px 11px`; body padding `0 11px 13px`, gap 10px.
- Drop-target `.col-over`: border var(--accent) + bg var(--accent-soft).
- Card: bg var(--card), border 1px var(--border), radius **10px**, padding 13px, transition
  border-color .14s/transform .06s/box-shadow .14s. Hover: border var(--disabled) + shadow
  `0 6px 20px -10px rgba(0,0,0,.6)`. Title 14.5px/600, line-height 1.32.
- Voice ring `.vscore`: circle, grid place-items center; inner `.vscore-inner` bg var(--card),
  size calc(100% - 5px), JetBrains Mono 10.5px/500. Empty `.vscore-empty`: 1.5px dashed border,
  var(--text-3), 13px.

### Detail drawer
- width **400px**, max-width 92vw, bg var(--card), border-left 1px var(--border), z-index 50,
  fixed full-height-right. Slide-in transition **.26s cubic-bezier(.2,.8,.2,1)**; shadow
  `-20px 0 60px -20px rgba(0,0,0,.7)`. Header padding `18px 20px`; body `22px 20px`.
- Close `.x`: 30×30px, radius 7px, var(--text-3), hover bg var(--hover)+var(--text).
- Title: DM Serif Display 23px/400, line-height 1.18, margin `10px 0 20px`.
- Meta row `.dm-row`: padding `12px 0`, gap 14px, border-bottom 1px var(--border); label
  `.dm-k` width 96px, 12px uppercase, letter-spacing .5px, var(--text-3)/600.
- Scrim: rgba(0,0,0,.55), z-index 40, fade .2s ease.

### Empty state
- Mark tile: 64×64px, radius 18px, grid center, 28px glyph, color var(--accent),
  bg var(--accent-soft), border 1px var(--accent), margin-bottom 22px.
- Heading: DM Serif Display 34px/400, line-height 1.1. Paragraph: var(--text-2) 15px,
  line-height 1.55, margin-top 12px, max-width 520px.

### Editor
- Top bar `.ed-top`: height **58px**, padding `0 22px`, gap 16px, border-bottom 1px var(--border).
- Stage dot `.ed-stage-dot`: 10×10px circle, 1.5px border var(--border), bg var(--base);
  on → bg+border var(--text-3); current `.cur` → 12×12px. Line `.ed-stage-line`: 18×1.5px,
  bg var(--border); on → var(--text-3).
- Manuscript `.manuscript`: max-width **680px**, margin 0 auto, padding `46px 32px 120px`.
  Title `.ms-title`: DM Serif Display 40px/400, line-height 1.12, outline none.
  Subtitle `.ms-sub`: 19px/400, line-height 1.45, var(--text-2), margin-top 14px.
  Body `.ms-prose`: 17.5px, line-height 1.75, color **#dcdce2** (h2 = serif 25px per README).
  Idea `.ms-idea`: centered, padding `48px 20px`, 1px dashed var(--border), radius var(--r),
  bg #0c0c0e; mark 24px var(--accent), margin-bottom 14px.
- Side panel: width 340px, bg #0c0c0e (README). Voice meter: label + big mono value colored by
  band + track/fill bar + band note. Sidecar bubbles: assistant var(--elev)+border, user brand
  bg + dark text; 3-dot "blink" keyframe (0/60/100% opacity .3).

### Publish modal
- Scrim `.pub-scrim`: rgba(0,0,0,.66), z-index 200, flex center, padding 32px, fade .2s.
- Modal `.pub`: width **1080px**, max-width 100%, max-height **90vh**, bg var(--card),
  border 1px var(--border), radius **16px**, shadow `0 30px 90px -30px rgba(0,0,0,.8)`,
  pop-in **.24s cubic-bezier(.2,.8,.2,1)** (scale .97 + translateY 8px + opacity .4 → 1).
- Header padding `20px 24px`, border-bottom; title DM Serif Display 24px/400; sub 13.5px var(--text-2).
- Grid `.pub-grid`: **340px 1fr**, flex 1, min-height 0.
- Destinations `.pub-dests`: border-right, overflow-y auto, padding 16px, gap 8px, column.
  Row `.dest`: padding 12px, 1px var(--border), radius 10px, bg #0c0c0e, transition .14s;
  hover border var(--disabled); selected `.on` border var(--accent)+bg var(--accent-soft).
- Preview `.pub-preview`: bg **#08080a**, column. Tabs `.pub-tabs`: gap 4px, padding `12px 16px 0`;
  tab `.pub-tab` padding `7px 12px`, 12.5px/500 var(--text-2), border 1px transparent; selected
  `.on` bg var(--card)+color var(--text)+border var(--border) (bottom border = card). Canvas
  `.pub-canvas`: flex 1, overflow-y auto, padding `0 20px 20px`. Schedule input: bg #0c0c0e,
  1px var(--border), radius 8px, 13px, padding `8px 10px`, var(--text) (color-scheme dark).
- Char/thread limits used: Twitter 280, LinkedIn 3000 (reconcile thread split limit — README
  says ≤270, prototype default 270; pick one, test boundary).

### Keyframes / motion
- `fade` .2s ease; `slidein` .26s cubic-bezier(.2,.8,.2,1); `popin` .24s cubic-bezier(.2,.8,.2,1);
  `blink` 3-dot thinking (0/60/100% opacity .3); `spin` rotate 360deg (publish spinner).
  Honor `prefers-reduced-motion` via a global CSS media query zeroing durations.

---

## B. Current Angular signatures (verbatim — what to reuse / not break)

### ContentStore (`stores/content.store.ts`, signalStore, providedIn root)
State: `contents: Content[]`, `totalCount`, `page`, `pageSize`, `filters: Partial<ContentFilterState>`,
`viewMode: 'list' | 'grid'`, `loading`, `error`.
Computed: `totalPages`, `hasNextPage`, `hasPreviousPage`.
Methods: `loadContents()`, `setFilter<K>(key, value)`, `setPage(page)`, `deleteContent(id)`, `toggleView()`.
> Gaps for redesign: extend `viewMode` to `'board'|'grid'|'table'`; add load-all + per-status
> counts (computed); add `activeStatus` concept; add `setStatus(id, status)` optimistic mutation;
> client-side title+tags search (debounced 300ms).

### ContentEditorStore (`stores/content-editor.store.ts`, signalStore)
`ChatMessage = { role:'user'|'assistant'; content:string; timestamp:string }`.
State: `content: ContentDetail | null`, `isDirty`, `isSaving`, `chatMessages: ChatMessage[]`,
`isStreaming`, `currentTokens`, `loading`, `error`.
Computed: `hasContent`, `canAutoSave` (`isDirty && !isSaving && content!==null`),
`statusActions` (switch on status → action-name string[]: Idea/Draft→['submitForReview'],
Review→['approve','requestChanges'], Approved→['schedule','publish'], Scheduled→['unschedule'],
Published→['unpublish'], Archived→['restore']).
Methods: `loadContent(id)`, `updateField<K>(field, value)`, `autoSave()`, `addChatMessage(text)`,
`appendToken`/`completeGeneration`/`applyToEditor(text)` (writes `body`).
> autoSave PUTs `update(id, {title,body,tags,contentType,primaryPlatform,targetPlatforms,
> lastUpdatedAt})` — only `title`+`body` are text fields. subtitle/byline are derived, not saved.

### ContentService (`services/content.service.ts`, baseUrl `/api/content`)
`list(filter,page,pageSize)→PagedResult<Content>`, `get(id)→ContentDetail`,
`create(CreateContentRequest)→string`, `update(id,UpdateContentRequest)→void`, `delete(id)→void`,
`draft(id,DraftContentRequest)`, `crossPost(id,CrossPostRequest)→string`,
`approve/submitForReview/requestChanges(id)→void`, `schedule(id,ScheduleContentRequest)→void`,
`unschedule(id)`, `publish(id, PublishRequest?)→void`, `unpublish(id)`, `restore(id)`,
`voiceCheck(id)→VoiceCheckResult`, `getPublishStatus(id)→PublishStatusResponse`,
`retryPlatform(id,platform)→void`, `getPlatforms()→PlatformConnectionStatus[]`.
> NOTE: `voiceCheck(id)` EXISTS — a live voice re-check IS possible (gap analysis earlier said
> none existed; corrected here). The side-panel voice meter can call `voiceCheck` on demand.
> `getPlatforms()` gives live `isConnected` — drive delivery badges from this, not hardcoded.

### Model (`models/content.model.ts`)
Enums: `ContentStatus`{Idea,Draft,Review,Approved,Scheduled,Published,Archived};
`ContentType`{BlogPost='Blog',LinkedInPost,Tweet,ThreadedTweet,SubstackNewsletter,RedditPost,
YouTubeVideo,YouTubeShort}; `Platform`{Blog,Medium,Substack,LinkedIn,Twitter,Reddit,YouTube};
`PublishStatus`{Pending,Formatting,Published,Failed}.
`Content`: id,title,contentType,status,primaryPlatform,targetPlatforms[],voiceScore:number|null,
tags[],createdAt,updatedAt,scheduledAt,publishedAt,platformPublishes[].
`ContentDetail extends Content`: body:string, viralityPrediction, sourceIdeaId, parentContentId,
platformPublishes:PlatformPublish[], children[].
`PlatformPublish`: id,platform,publishStatus,publishedUrl,publishedAt,retryCount,nextRetryAt.
`VoiceCheckResult`: {score, feedback}. `UpdateContentRequest`: {title,body,tags,contentType,
primaryPlatform,targetPlatforms,lastUpdatedAt}. `PublishRequest`: {targetPlatforms?}.
`ScheduleContentRequest`: {scheduledAt, targetPlatforms?}. `PublishStatusResponse`:
{contentId, primaryPlatform, platformStatuses:PlatformPublish[]}.
`PlatformConnectionStatus`: {platform,isConnected,isExpiring,expiresAt,capabilities}.
`PlatformCapabilities`: {maxCharacters,supportsMarkdown,supportsHtml,supportsImages,
supportsScheduling,supportsThreads}.
`ContentFilterState`: {status?,platform?,contentType?,dateFrom?,dateTo?,search?}.
Consts: `PUBLISHABLE_PLATFORMS`=[Blog,Medium,Substack,LinkedIn,Twitter];
`PLATFORM_CHAR_LIMITS`={Twitter:280, LinkedIn:3000}.

### Routes (`content.routes.ts`)
`''`→ContentListComponent (lazy); `'new'`→ContentEditorComponent; `':id'`→ContentEditorComponent.

### Current PrimeNG usage (match this import style for new overlays)
DrawerModule (sidecar uses `p-drawer`), ButtonModule, TextareaModule, SelectModule (`p-select`),
DatePickerModule (`p-datepicker`), TagModule, ChipModule, InputTextModule, KnobModule (voice),
SplitterModule (editor split — to be removed), TooltipModule. All standalone imports.

### Global styles (current — the prereq target)
`src/styles.scss`: reset + body `background:#0f1117; color:#e1e4e8` + scrollbar #30363d/#161b22.
**GitHub-dark, imports none of the `styles/` partials.** `_variables.scss` has correct
terracotta `$vars` (see list below) but is orphaned. `$sidebar-width:200px` (spec wants 212).

---

## B-2. Verified web research (current APIs)

### Angular CDK DragDrop (kanban) — Angular 19 → cdk ^19.2.x
- Standalone directives from `@angular/cdk/drag-drop`: `CdkDropListGroup`, `CdkDropList`, `CdkDrag`
  (import directly in component `imports`; `DragDropModule` optional).
- Multi-column: wrap columns in `cdkDropListGroup` (auto-connects sibling drop lists — no manual
  `cdkDropListConnectedTo`). Each column = `cdkDropList` with `[cdkDropListData]` + `(cdkDropListDropped)`.
- `CdkDragDrop<T>` fields: `previousContainer`, `container` (compare by ref), `previousIndex`,
  `currentIndex`, `item.data`, `isPointerOverContainer`, `distance`.
- **CRITICAL signals pitfall:** `moveItemInArray`/`transferArrayItem` **mutate in place** →
  silently fail with `computed()`/signal arrays (no new reference). Pattern: bind columns to a
  `computed()` grouping for the template, but in the drop handler COPY the arrays
  (`[...container.data]`), run the CDK util on copies, then call a store method that `.set()`s new
  state. **Never bind `[cdkDropListData]` to a computed and mutate it.** For our case, the drop
  handler should just call `ContentStore.setStatus(card.id, targetColumnStatus)` (optimistic) —
  the computed grouping re-derives automatically.
- Empty `cdkDropList` still accepts drops if it has `min-height`. `*cdkDragPlaceholder` /
  `*cdkDragPreview` customize the gap / floating element.

### marked — tokens + plain-text strip
- `marked.lexer(md)` → `Token[]` (synchronous). heading `{type:'heading',depth,text,tokens}`,
  paragraph `{type:'paragraph',text,tokens}`, plus blockquote/list/code/table/space/hr.
- Map depth→`h1..h6`, prose→`p` for the `{type,text}` block array the previews render.
- **Plain text for char budgets:** do NOT regex-strip or count rendered HTML (URLs/`**`/backticks
  inflate). Use `marked.walkTokens(lexer(md), cb)` and concatenate leaf `text`/`codespan`/`space`.
  Recurse inline `tokens`, exclude link `href`.
- Sync by default; safe in component code.
- **Recommendation:** keep marked (lexer gives exactly the AST needed; avoids DOM). Confirm the
  marked version resolved via ngx-markdown exposes `lexer`/`walkTokens` (marked ≥ recent majors do).

### contenteditable / prose surface with signals — KEY DECISION
- Hand-rolled contenteditable + HTML→markdown serialization is the **fragile path**: caret-jump on
  re-render, inconsistent browser markup, paste XSS/style drift, round-trip fidelity loss.
- Two robust options:
  1. **TipTap (ProseMirror)** headless, no toolbar: load markdown → doc → edit → serialize to
     markdown. Eliminates the caret/paste/drift bug class. Heavier dep; wrap instance in an Angular
     component, bridge to a signal. **Matches BOTH the design (clean prose, no visible syntax) AND
     the locked decision (markdown source of truth).**
  2. **Styled `<textarea>` of raw markdown** + live preview pane: zero caret/XSS bugs, trivial
     binding, no new dep — BUT shows raw markdown syntax, which **contradicts the design's clean
     prose surface**.
- **Tension to resolve in interview:** the design demands a clean prose surface (title 40px serif,
  body 17.5px prose, "No markdown"), but the locked decision keeps markdown as source of truth and
  bans free HTML. The only option satisfying both is a structured editor (TipTap) — at the cost of
  a new dependency. A plain contenteditable is the risky middle. Decide: TipTap dep vs accept
  raw-markdown textarea vs accept contenteditable risk.

### PrimeNG 20 overlays (verified v20)
- Modal: `p-dialog` / `DialogModule` (`primeng/dialog`): `[modal]`, `[draggable]="false"`,
  `[(visible)]`, `[style]="{width:'1080px'}"`, `[contentStyle]="{maxHeight:'90vh'}"`, `appendTo="body"`,
  named templates `<ng-template #header>`/`#footer`.
- Right drawer: `p-drawer` / `DrawerModule` (`primeng/drawer`): `[(visible)]`, `position="right"`,
  `[modal]`. (Replaced `p-sidebar`; sidecar already uses it.)
- Filters popover: `p-popover` / `PopoverModule` (`primeng/popover`): template ref + `op.toggle($event)`
  (replaced `p-overlayPanel`; `$event` required to anchor).
- Reduced motion: no per-overlay flag — handle globally via `@media (prefers-reduced-motion: reduce)`.
> Caveat: the publish modal/drawer/popover could be built as PrimeNG components OR as bespoke
> token-styled overlays to hit the exact prototype CSS (1080px, custom shadows/animations). Decide
> per-overlay: PrimeNG (consistency, less code) vs bespoke (pixel-exact). Likely bespoke for the
> publish modal (very custom), PrimeNG drawer for the detail drawer, PrimeNG popover for Filters.

---

## SCSS `$variables` currently defined (`styles/_variables.scss` — orphaned)
brand-primary #c87156 / hover #d4836a / active #b5624a; surface base #0e0e10 / card #141418 /
elevated #1a1a20 / hover #22222a / border #2c2c36 / disabled #3a3a46; text primary #f0f0f5 /
secondary #8a8a96 / muted #5a5a66; status draft #5a5a66 / review #c87156 / approved #4ade80 /
scheduled #60a5fa / published #4ade80 / failed #f87171; score success #4ade80 / warning #fbbf24 /
danger #f87171; fonts body 'DM Sans' / display 'DM Serif Display' / mono 'JetBrains Mono';
space 4..48; sidebar-width 200px / collapsed 56 / header 56 / sidecar 380.
> Add for redesign: `accent-soft`, status `idea #8a7df0` + `draft #8a8a96` (contrast) + fix
> `published #34d399`, sidebar #0b0b0d / inset #0c0c0e / publish-canvas #08080a, delivery badge
> pairs (auto #1f3a2b/#4ade80, manual #3a2f1c/#fbbf24, warn #3a2420/#f0935f), radius scale
> (12/10/8/99/16), sidebar-width→212. Expose ALL as `:root` CSS custom properties.

## Testing setup (for TDD step)
App uses Angular standalone + signals; tests are `*.spec.ts` colocated per component/store
(existing specs for store/service/components confirm Jasmine/Karma `ng test`). New components/
stores/util modules each get a colocated `.spec.ts`. Pure logic (thread splitter, markdown→blocks,
plain-text stripper, status-order/next-status, delivery-badge logic, voice band) is the
highest-value unit-test surface. Component tests cover render-by-state + interaction.

## Open decisions to resolve in the interview (step 8)
1. **Editor prose surface tech:** TipTap dep vs raw-markdown textarea vs contenteditable risk.
2. **Overlay tech per surface:** PrimeNG vs bespoke token-styled (publish modal especially).
3. **Thread split limit:** 270 vs 280.
4. **marked sourcing:** rely on ngx-markdown's transitive marked vs add explicit `marked` dep.
5. **Scope/sequencing:** is the styling-foundation prereq (tokens + recolor) a separate first
   deliverable, and is the whole thing one branch?
