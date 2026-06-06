# Idea Bank + Daily Brief UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Daily Brief and Idea Bank pages on the app's obsidian/terracotta design tokens with a browse-focused Idea Bank and a daily-read + history Daily Brief.

**Architecture:** Pure Angular frontend (`PersonalBrandAssistant.Web`). Reuse the existing `IdeaStore` (NgRx signals) and `DigestService`; no API/DB changes. Author all styles against `var(--…)` tokens from `src/styles/_tokens.scss`. Introduce a shared `ScoreBadgeComponent` so score styling is identical across both pages.

**Tech Stack:** Angular 19 standalone components, NgRx signals, PrimeNG 20, SCSS design tokens, Jasmine/Karma + `HttpTestingController`.

---

## File structure

| File | Responsibility |
|---|---|
| `src/app/shared/score-badge/score-badge.component.ts` (new) | Reusable color-coded score pill (threshold tokens) |
| `src/app/features/ideas/components/idea-card/idea-card.component.ts` (rewrite) | Idea card on tokens, uses ScoreBadge |
| `src/app/features/ideas/components/idea-list/idea-list.component.ts` (restyle) | Dense scannable rows |
| `src/app/features/ideas/components/active-filter-chips/active-filter-chips.component.ts` (new) | Removable applied-filter chips |
| `src/app/features/ideas/components/score-distribution/score-distribution.component.ts` (new) | Per-band counts over current page |
| `src/app/features/ideas/ideas.component.ts` (rewrite styles + header) | Shell on tokens, sort control, chips row |
| `src/app/features/digest/components/brief-detail/brief-detail.component.ts` (new) | Hero + ranked list of one digest |
| `src/app/features/digest/components/brief-history/brief-history.component.ts` (new) | Timeline of past digests |
| `src/app/features/digest/pages/daily-brief/daily-brief.component.ts` (rewrite) | Two-pane shell wiring history + detail |

Run all test commands from `src/PersonalBrandAssistant.Web`. Test runner: `npx ng test --watch=false --browsers=ChromeHeadless --include=<path>`.

Token reference (from `src/styles/_tokens.scss`, available globally as CSS vars): `--surface-base #0e0e10`, `--surface-card #141418`, `--surface-elevated #1a1a20`, `--surface-hover #22222a`, `--surface-border #2c2c36`, `--text-primary #f0f0f5`, `--text-secondary #8a8a96`, `--text-muted #5a5a66`, `--brand-primary #c87156`, score thresholds `--score-success #4ade80` / `--score-warning #fbbf24` / `--score-danger #f87171` (defined in `_variables.scss`; add to `_tokens.scss` in Task 1), radius `--r 12px` / `--r-control 8px` / `--r-pill 99px`.

---

## Task 1: Shared ScoreBadge component

**Files:**
- Modify: `src/styles/_tokens.scss` (add score tokens to `:root`)
- Create: `src/app/shared/score-badge/score-badge.component.ts`
- Test: `src/app/shared/score-badge/score-badge.component.spec.ts`

- [ ] **Step 1: Add score tokens to `_tokens.scss`**

In `src/styles/_tokens.scss`, inside the `:root { … }` block (after the `--voice-low` line), add:

```scss
  // score bands (mirror $score-* in _variables.scss)
  --score-success: #4ade80;
  --score-warning: #fbbf24;
  --score-danger: #f87171;
```

- [ ] **Step 2: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScoreBadgeComponent } from './score-badge.component';

describe('ScoreBadgeComponent', () => {
  let fixture: ComponentFixture<ScoreBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ScoreBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(ScoreBadgeComponent);
  });

  function render(score: number | null) {
    fixture.componentRef.setInput('score', score);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows score out of 10', () => {
    expect(render(8).textContent).toContain('8/10');
  });

  it('uses the success band for scores >= 7', () => {
    expect(render(7).querySelector('.score-badge')?.classList).toContain('band-success');
  });

  it('uses the warning band for scores 4-6', () => {
    expect(render(6).querySelector('.score-badge')?.classList).toContain('band-warning');
    expect(render(4).querySelector('.score-badge')?.classList).toContain('band-warning');
  });

  it('uses the danger band for scores < 4', () => {
    expect(render(3).querySelector('.score-badge')?.classList).toContain('band-danger');
  });

  it('renders nothing when score is null', () => {
    expect(render(null).querySelector('.score-badge')).toBeNull();
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/shared/score-badge/score-badge.component.spec.ts`
Expected: FAIL — cannot find module `./score-badge.component`.

- [ ] **Step 4: Write minimal implementation**

```typescript
import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-score-badge',
  standalone: true,
  template: `
    @if (score() !== null) {
      <span class="score-badge" [class]="band()" [title]="title()">{{ score() }}/10</span>
    }
  `,
  styles: [`
    .score-badge {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: var(--r-pill);
      cursor: default;
      white-space: nowrap;
    }
    .band-success { background: color-mix(in srgb, var(--score-success) 18%, transparent); color: var(--score-success); }
    .band-warning { background: color-mix(in srgb, var(--score-warning) 18%, transparent); color: var(--score-warning); }
    .band-danger  { background: color-mix(in srgb, var(--score-danger) 18%, transparent);  color: var(--score-danger); }
  `],
})
export class ScoreBadgeComponent {
  readonly score = input.required<number | null>();
  readonly title = input<string>('');
  readonly band = computed(() => {
    const s = this.score();
    if (s === null) return '';
    if (s >= 7) return 'band-success';
    if (s >= 4) return 'band-warning';
    return 'band-danger';
  });
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/shared/score-badge/score-badge.component.spec.ts`
Expected: PASS (5 specs).

- [ ] **Step 6: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/shared/score-badge src/PersonalBrandAssistant.Web/src/styles/_tokens.scss
git commit -m "feat(web): shared ScoreBadge component on score-band tokens"
```

---

## Task 2: Rebuild IdeaCard on tokens + ScoreBadge

**Files:**
- Modify: `src/app/features/ideas/components/idea-card/idea-card.component.ts` (full rewrite)
- Test: `src/app/features/ideas/components/idea-card/idea-card.component.spec.ts` (keep existing `data-testid` selectors green)

- [ ] **Step 1: Check the existing spec's expectations**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-card/idea-card.component.spec.ts`
Expected: PASS currently. Note the `data-testid` values it relies on: `idea-card`, `idea-score-badge`, `save-btn`, `dismiss-btn`, `create-content-btn`. The rewrite MUST preserve these.

- [ ] **Step 2: Add a failing test for the token-based score badge usage**

Append to `idea-card.component.spec.ts` inside the existing `describe`:

```typescript
it('renders the shared score badge with the score', () => {
  component.idea = signalInput({ ...baseIdea, score: 9 }); // use the spec's existing idea factory
  fixture.detectChanges();
  const badge = fixture.nativeElement.querySelector('app-score-badge .score-badge');
  expect(badge?.textContent).toContain('9/10');
  expect(badge?.classList).toContain('band-success');
});
```

If the existing spec uses a different idea-input mechanism, mirror it (the spec already sets `idea` via `fixture.componentRef.setInput('idea', …)`). Use that exact mechanism instead of `signalInput`.

- [ ] **Step 3: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-card/idea-card.component.spec.ts`
Expected: FAIL — `app-score-badge` not found (card still uses the inline `.idea-score-badge`).

- [ ] **Step 4: Rewrite the component**

Replace the entire file with:

```typescript
import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { Idea } from '../../../../models/idea.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-idea-card',
  standalone: true,
  imports: [DatePipe, ButtonModule, TagModule, TooltipModule, ScoreBadgeComponent],
  template: `
    <div class="idea-card" data-testid="idea-card">
      @if (idea().thumbnailUrl) {
        <img [src]="idea().thumbnailUrl" [alt]="idea().title" class="card-thumbnail" />
      }
      <div class="card-body">
        <div class="card-header">
          <span class="status-badge" [attr.data-status]="idea().status">{{ idea().status }}</span>
          <app-score-badge data-testid="idea-score-badge"
            [score]="idea().score" [title]="idea().scoreReason ?? ''" />
          <span class="source-name">{{ idea().sourceName }}</span>
          <span class="detected-at">{{ idea().detectedAt | date: 'shortDate' }}</span>
        </div>
        @if (idea().url) {
          <a [href]="idea().url" target="_blank" rel="noopener noreferrer" class="card-title-link">
            <h3 class="card-title">{{ idea().title }}</h3>
          </a>
        } @else {
          <h3 class="card-title">{{ idea().title }}</h3>
        }
        @if (idea().summary || idea().description) {
          <p class="card-summary">{{ truncate((idea().summary || idea().description)!, 140) }}</p>
        }
        @if (idea().tags.length > 0) {
          <div class="card-tags">
            @for (tag of idea().tags.slice(0, 3); track tag) {
              <span class="chip">{{ tag }}</span>
            }
            @if (idea().tags.length > 3) {
              <span class="more-tags">+{{ idea().tags.length - 3 }}</span>
            }
          </div>
        }
        <div class="card-actions">
          <p-button icon="pi pi-bookmark" severity="secondary" [text]="true" size="small"
            (onClick)="save.emit(idea().id)" data-testid="save-btn" pTooltip="Save" />
          <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small"
            (onClick)="dismiss.emit(idea().id)" data-testid="dismiss-btn" pTooltip="Dismiss" />
          <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small"
            (onClick)="createContent.emit(idea().id)" data-testid="create-content-btn" pTooltip="Create Content" />
        </div>
      </div>
    </div>
  `,
  styles: [`
    .idea-card {
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: var(--r);
      overflow: hidden;
      transition: border-color 0.2s, transform 0.2s;
      display: flex;
      flex-direction: column;
    }
    .idea-card:hover { border-color: var(--brand-primary); transform: translateY(-1px); }
    .card-thumbnail { width: 100%; height: 140px; object-fit: cover; }
    .card-body { padding: 14px; display: flex; flex-direction: column; gap: 8px; }
    .card-header { display: flex; align-items: center; gap: 8px; font-size: 12px; color: var(--text-secondary); flex-wrap: wrap; }
    .status-badge { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: var(--r-pill); text-transform: uppercase; color: var(--text-secondary); background: var(--surface-hover); }
    .status-badge[data-status='New'] { color: var(--status-idea); background: color-mix(in srgb, var(--status-idea) 16%, transparent); }
    .status-badge[data-status='Saved'] { color: var(--status-approved); background: color-mix(in srgb, var(--status-approved) 16%, transparent); }
    .status-badge[data-status='Dismissed'] { color: var(--score-danger); background: color-mix(in srgb, var(--score-danger) 16%, transparent); }
    .source-name { margin-left: auto; }
    .card-title-link { text-decoration: none; }
    .card-title-link:hover .card-title { color: var(--brand-primary); }
    .card-title { font-size: 15px; font-weight: 600; color: var(--text-primary); margin: 0; line-height: 1.35; transition: color 0.15s; }
    .card-summary { font-size: 13px; color: var(--text-secondary); margin: 0; line-height: 1.45; }
    .card-tags { display: flex; gap: 6px; flex-wrap: wrap; }
    .chip { font-size: 11px; color: var(--text-secondary); background: var(--surface-hover); border-radius: var(--r-pill); padding: 2px 10px; }
    .more-tags { font-size: 11px; color: var(--text-muted); align-self: center; }
    .card-actions { display: flex; gap: 4px; border-top: 1px solid var(--surface-border); padding-top: 8px; margin-top: 4px; }
  `],
})
export class IdeaCardComponent {
  readonly idea = input.required<Idea>();
  readonly save = output<string>();
  readonly dismiss = output<string>();
  readonly createContent = output<string>();

  truncate(text: string, maxLength: number): string {
    return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
  }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-card/idea-card.component.spec.ts`
Expected: PASS (existing specs + new badge spec). If an existing spec asserted the old `.idea-score-badge` text directly, update it to query `app-score-badge`.

- [ ] **Step 6: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card
git commit -m "feat(web): rebuild IdeaCard on design tokens + ScoreBadge"
```

---

## Task 3: Restyle IdeaList to dense scannable rows

**Files:**
- Modify: `src/app/features/ideas/components/idea-list/idea-list.component.ts`
- Test: `src/app/features/ideas/components/idea-list/idea-list.component.spec.ts`

- [ ] **Step 1: Add a failing test for a score badge per row**

Add to the spec (mirror the existing input mechanism for `[ideas]`):

```typescript
it('shows a score badge for each scored idea row', () => {
  fixture.componentRef.setInput('ideas', [{ ...baseIdea, id: 'a', score: 9 }]);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('app-score-badge .band-success')).toBeTruthy();
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-list/idea-list.component.spec.ts`
Expected: FAIL — `app-score-badge` not present.

- [ ] **Step 3: Rewrite the list template + styles**

Open the file. Add `ScoreBadgeComponent` to `imports`. Replace the row template so each idea is one dense row:

```typescript
// imports: add
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';
import { DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
```

Template (replace the list body):

```html
<div class="idea-rows" data-testid="idea-list">
  @for (idea of ideas(); track idea.id) {
    <div class="idea-row">
      <app-score-badge [score]="idea.score" [title]="idea.scoreReason ?? ''" />
      <div class="row-main">
        @if (idea.url) {
          <a [href]="idea.url" target="_blank" rel="noopener noreferrer" class="row-title">{{ idea.title }}</a>
        } @else {
          <span class="row-title">{{ idea.title }}</span>
        }
        <div class="row-meta">
          <span class="source">{{ idea.sourceName }}</span>
          <span class="dot">·</span>
          <span>{{ idea.detectedAt | date: 'shortDate' }}</span>
        </div>
      </div>
      <div class="row-actions">
        <p-button icon="pi pi-bookmark" severity="secondary" [text]="true" size="small" (onClick)="save.emit(idea.id)" data-testid="save-btn" pTooltip="Save" />
        <p-button icon="pi pi-times" severity="secondary" [text]="true" size="small" (onClick)="dismiss.emit(idea.id)" data-testid="dismiss-btn" pTooltip="Dismiss" />
        <p-button icon="pi pi-pencil" severity="secondary" [text]="true" size="small" (onClick)="createContent.emit(idea.id)" data-testid="create-content-btn" pTooltip="Create Content" />
      </div>
    </div>
  }
</div>
```

Styles:

```scss
.idea-rows { display: flex; flex-direction: column; }
.idea-row {
  display: flex; align-items: center; gap: 12px;
  padding: 10px 12px; border-bottom: 1px solid var(--surface-border);
  transition: background 0.15s;
}
.idea-row:hover { background: var(--surface-hover); }
.row-main { flex: 1; min-width: 0; }
.row-title { color: var(--text-primary); font-weight: 600; font-size: 14px; text-decoration: none; display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
a.row-title:hover { color: var(--brand-primary); }
.row-meta { font-size: 12px; color: var(--text-secondary); display: flex; gap: 6px; }
.row-actions { display: flex; gap: 2px; opacity: 0; transition: opacity 0.15s; }
.idea-row:hover .row-actions { opacity: 1; }
```

Ensure the component keeps its existing `ideas` input and `save`/`dismiss`/`createContent` outputs.

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-list/idea-list.component.spec.ts`
Expected: PASS. Update any existing assertion that referenced old row markup.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list
git commit -m "feat(web): dense token-styled idea list rows with ScoreBadge"
```

---

## Task 4: Active filter chips

**Files:**
- Create: `src/app/features/ideas/components/active-filter-chips/active-filter-chips.component.ts`
- Test: `src/app/features/ideas/components/active-filter-chips/active-filter-chips.component.spec.ts`

- [ ] **Step 1: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActiveFilterChipsComponent } from './active-filter-chips.component';
import { IdeaFilterState } from '../../../../models/idea.model';

const empty: IdeaFilterState = {
  status: null, sourceId: null, category: null, tags: [],
  dateFrom: null, dateTo: null, searchText: null, minScore: null,
};

describe('ActiveFilterChipsComponent', () => {
  let fixture: ComponentFixture<ActiveFilterChipsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ActiveFilterChipsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ActiveFilterChipsComponent);
  });

  it('renders a chip per active filter', () => {
    fixture.componentRef.setInput('filter', { ...empty, minScore: 7, category: 'AI' });
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('[data-testid="filter-chip"]');
    expect(chips.length).toBe(2);
  });

  it('emits clear with the filter key when a chip is removed', () => {
    let cleared: keyof IdeaFilterState | undefined;
    fixture.componentRef.setInput('filter', { ...empty, minScore: 7 });
    fixture.componentInstance.clear.subscribe((k) => (cleared = k));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="filter-chip"] button') as HTMLButtonElement).click();
    expect(cleared).toBe('minScore');
  });

  it('renders nothing when no filters are active', () => {
    fixture.componentRef.setInput('filter', empty);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="filter-chip"]').length).toBe(0);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/active-filter-chips/active-filter-chips.component.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the implementation**

```typescript
import { Component, computed, input, output } from '@angular/core';
import { IdeaFilterState } from '../../../../models/idea.model';

interface Chip { key: keyof IdeaFilterState; label: string; }

@Component({
  selector: 'app-active-filter-chips',
  standalone: true,
  template: `
    @if (chips().length > 0) {
      <div class="chips">
        @for (chip of chips(); track chip.key) {
          <span class="filter-chip" data-testid="filter-chip">
            {{ chip.label }}
            <button type="button" aria-label="Remove filter" (click)="clear.emit(chip.key)">
              <i class="pi pi-times"></i>
            </button>
          </span>
        }
      </div>
    }
  `,
  styles: [`
    .chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .filter-chip {
      display: inline-flex; align-items: center; gap: 6px;
      font-size: 12px; color: var(--text-primary);
      background: var(--accent-soft); border: 1px solid var(--surface-border);
      border-radius: var(--r-pill); padding: 3px 6px 3px 10px;
    }
    .filter-chip button { background: none; border: none; color: var(--text-secondary); cursor: pointer; display: flex; padding: 0; }
    .filter-chip button:hover { color: var(--text-primary); }
    .filter-chip i { font-size: 10px; }
  `],
})
export class ActiveFilterChipsComponent {
  readonly filter = input.required<IdeaFilterState>();
  readonly clear = output<keyof IdeaFilterState>();

  readonly chips = computed<Chip[]>(() => {
    const f = this.filter();
    const out: Chip[] = [];
    if (f.searchText) out.push({ key: 'searchText', label: `Search: ${f.searchText}` });
    if (f.status) out.push({ key: 'status', label: `Status: ${f.status}` });
    if (f.category) out.push({ key: 'category', label: `Category: ${f.category}` });
    if (f.sourceId) out.push({ key: 'sourceId', label: 'Source' });
    if (f.minScore != null) out.push({ key: 'minScore', label: `Score ≥ ${f.minScore}` });
    if (f.dateFrom) out.push({ key: 'dateFrom', label: `From ${f.dateFrom}` });
    if (f.dateTo) out.push({ key: 'dateTo', label: `To ${f.dateTo}` });
    for (const tag of f.tags) out.push({ key: 'tags', label: `#${tag}` });
    return out;
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/active-filter-chips/active-filter-chips.component.spec.ts`
Expected: PASS (3 specs).

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/components/active-filter-chips
git commit -m "feat(web): active filter chips component"
```

---

## Task 5: Score distribution (current page)

**Files:**
- Create: `src/app/features/ideas/components/score-distribution/score-distribution.component.ts`
- Test: `src/app/features/ideas/components/score-distribution/score-distribution.component.spec.ts`

> Per the spec, this reflects only the **current results page** and is labeled "this page". If at implementation it feels low-value, skip this task (YAGNI) and remove its usage in Task 6.

- [ ] **Step 1: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScoreDistributionComponent } from './score-distribution.component';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

function idea(score: number | null): Idea {
  return { id: crypto.randomUUID(), title: 't', description: null, url: null, sourceName: 's',
    category: null, summary: null, thumbnailUrl: null, status: IdeaStatus.New, tags: [],
    detectedAt: '2026-06-06', hasSavedDetails: false, score, scoreReason: null, isDuplicate: false };
}

describe('ScoreDistributionComponent', () => {
  let fixture: ComponentFixture<ScoreDistributionComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ScoreDistributionComponent] }).compileComponents();
    fixture = TestBed.createComponent(ScoreDistributionComponent);
  });

  it('counts ideas per band, ignoring unscored', () => {
    fixture.componentRef.setInput('ideas', [idea(9), idea(8), idea(5), idea(2), idea(null)]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="band-high"]')?.textContent).toContain('2');
    expect(el.querySelector('[data-testid="band-mid"]')?.textContent).toContain('1');
    expect(el.querySelector('[data-testid="band-low"]')?.textContent).toContain('1');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/score-distribution/score-distribution.component.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the implementation**

```typescript
import { Component, computed, input } from '@angular/core';
import { Idea } from '../../../../models/idea.model';

@Component({
  selector: 'app-score-distribution',
  standalone: true,
  template: `
    <div class="dist">
      <div class="dist-head">Scores <span class="scope">(this page)</span></div>
      <div class="bands">
        <div class="band high" data-testid="band-high"><b>{{ counts().high }}</b><span>7-10</span></div>
        <div class="band mid" data-testid="band-mid"><b>{{ counts().mid }}</b><span>4-6</span></div>
        <div class="band low" data-testid="band-low"><b>{{ counts().low }}</b><span>0-3</span></div>
      </div>
    </div>
  `,
  styles: [`
    .dist { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 12px; }
    .dist-head { font-size: 12px; color: var(--text-secondary); margin-bottom: 8px; }
    .scope { color: var(--text-muted); }
    .bands { display: flex; gap: 8px; }
    .band { flex: 1; text-align: center; border-radius: var(--r-control); padding: 8px 4px; background: var(--surface-elevated); }
    .band b { display: block; font-size: 18px; }
    .band span { font-size: 11px; color: var(--text-secondary); }
    .band.high b { color: var(--score-success); }
    .band.mid b { color: var(--score-warning); }
    .band.low b { color: var(--score-danger); }
  `],
})
export class ScoreDistributionComponent {
  readonly ideas = input.required<Idea[]>();
  readonly counts = computed(() => {
    let high = 0, mid = 0, low = 0;
    for (const i of this.ideas()) {
      if (i.score === null) continue;
      if (i.score >= 7) high++; else if (i.score >= 4) mid++; else low++;
    }
    return { high, mid, low };
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/score-distribution/score-distribution.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/components/score-distribution
git commit -m "feat(web): per-page score distribution widget"
```

---

## Task 6: Idea Bank shell — tokens, sort control, chips, distribution

**Files:**
- Modify: `src/app/features/ideas/ideas.component.ts`
- Test: `src/app/features/ideas/ideas.component.spec.ts`

- [ ] **Step 1: Add a failing test for the sort control + chips wiring**

Add to the spec (the spec already provides `IdeaStore`; if it mocks it, extend the mock with `setSort` and `setFilter` spies):

```typescript
it('calls store.setSort when the sort option changes', () => {
  const spy = spyOn(store, 'setSort');
  component.onSortChange('score');
  expect(spy).toHaveBeenCalledWith({ field: 'score', direction: 'desc' });
});

it('clears a single filter key via the chips', () => {
  const spy = spyOn(store, 'setFilter');
  component.onClearFilter('minScore');
  expect(spy).toHaveBeenCalledWith({ minScore: null });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/ideas.component.spec.ts`
Expected: FAIL — `onSortChange` / `onClearFilter` not defined.

- [ ] **Step 3: Implement the methods + template + token styles**

In `ideas.component.ts`:

Add imports:
```typescript
import { SelectModule } from 'primeng/select';
import { ActiveFilterChipsComponent } from './components/active-filter-chips/active-filter-chips.component';
import { ScoreDistributionComponent } from './components/score-distribution/score-distribution.component';
import { IdeaFilterState } from '../../models/idea.model';
```
Add `SelectModule`, `ActiveFilterChipsComponent`, `ScoreDistributionComponent` to `imports`.

Add methods to the class:
```typescript
readonly sortOptions = [
  { label: 'Newest', value: 'detectedAt' },
  { label: 'Highest score', value: 'score' },
  { label: 'Source', value: 'sourceName' },
];
sortField = 'detectedAt';

onSortChange(field: string): void {
  this.sortField = field;
  this.store.setSort({ field, direction: 'desc' });
}

onClearFilter(key: keyof IdeaFilterState): void {
  this.store.setFilter({ [key]: key === 'tags' ? [] : null } as Partial<IdeaFilterState>);
}
```

In the template `header-controls`, add the sort select next to the search/view-toggle:
```html
<p-select [options]="sortOptions" optionLabel="label" optionValue="value"
  [(ngModel)]="sortField" (onChange)="onSortChange($event.value)"
  data-testid="sort-select" styleClass="sort-select" />
```
Below `header-controls`, add the chips row:
```html
<app-active-filter-chips [filter]="store.filter()" (clear)="onClearFilter($event)" />
```
In the `suggestions-sidebar`, above `app-smart-suggestions`, add:
```html
<app-score-distribution [ideas]="store.ideas()" />
```

Replace ALL hardcoded hex in the `styles` block with tokens: `#0d1117`/background → `var(--surface-base)`; `#f0f6fc` → `var(--text-primary)`; `#8b949e` → `var(--text-secondary)`; `#58a6ff` (links) → `var(--brand-primary)`; `#21262d`/`#30363d` borders → `var(--surface-border)`. Example for the header title and link:
```scss
.header-top h1 { font-size: 24px; font-weight: 600; margin: 0; color: var(--text-primary); }
.manage-sources-link { font-size: 13px; color: var(--brand-primary); text-decoration: none; display: flex; align-items: center; gap: 4px; }
.suggestions-sidebar { padding: 16px; border-left: 1px solid var(--surface-border); overflow-y: auto; display: flex; flex-direction: column; gap: 16px; }
.loading-state { text-align: center; padding: 48px; color: var(--text-secondary); }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/ideas.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.component.ts
git commit -m "feat(web): Idea Bank shell on tokens with sort, filter chips, distribution"
```

---

## Task 7: Restyle the filter sidebar on tokens (incl. score range)

**Files:**
- Modify: `src/app/features/ideas/components/idea-filter-sidebar/idea-filter-sidebar.component.ts`
- Test: `src/app/features/ideas/components/idea-filter-sidebar/idea-filter-sidebar.component.spec.ts`

- [ ] **Step 1: Confirm current behavior is green**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-filter-sidebar/idea-filter-sidebar.component.spec.ts`
Expected: PASS. Read the component to see which filters it already exposes and whether it has a min-score control.

- [ ] **Step 2: Add a failing test for a score-range control (only if absent)**

If the sidebar lacks a min-score control, add:
```typescript
it('sets minScore on the store when the score slider changes', () => {
  const spy = spyOn(store, 'setFilter');
  component.onMinScoreChange(7);
  expect(spy).toHaveBeenCalledWith({ minScore: 7 });
});
```
If the control already exists, skip to Step 4 (pure restyle) and only re-token the styles.

- [ ] **Step 3: Add the control**

Add `import { SliderModule } from 'primeng/slider';` to imports. Add:
```typescript
minScore = 0;
onMinScoreChange(value: number): void {
  this.minScore = value;
  this.store.setFilter({ minScore: value > 0 ? value : null });
}
```
Template, in a "Score" group:
```html
<div class="filter-group">
  <label>Min score: {{ minScore }}</label>
  <p-slider [(ngModel)]="minScore" [min]="0" [max]="10" (onSlideEnd)="onMinScoreChange($event.value)" />
</div>
```

- [ ] **Step 4: Re-token all styles**

Replace any hardcoded hex in the sidebar styles with tokens (`--surface-*`, `--text-*`, `--brand-primary`, `--surface-border`). Group headers use `color: var(--text-secondary)`, active items `color: var(--brand-primary)`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/ideas/components/idea-filter-sidebar/idea-filter-sidebar.component.spec.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-filter-sidebar
git commit -m "feat(web): re-token filter sidebar + score range control"
```

---

## Task 8: Daily Brief — BriefDetail component

**Files:**
- Create: `src/app/features/digest/components/brief-detail/brief-detail.component.ts`
- Test: `src/app/features/digest/components/brief-detail/brief-detail.component.spec.ts`

- [ ] **Step 1: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BriefDetailComponent } from './brief-detail.component';
import { Digest } from '../../models/digest.model';

const digest: Digest = {
  id: 'd1', date: '2026-06-05', title: 'AI Brief', intro: 'Top stories', itemCount: 2,
  createdAt: '2026-06-05T22:00:00Z',
  items: [
    { ideaId: 'a', rank: 1, score: 9, whyItMatters: 'Big', title: 'First', url: 'https://x/a' },
    { ideaId: 'b', rank: 2, score: 7, whyItMatters: 'Also', title: 'Second', url: null },
  ],
};

describe('BriefDetailComponent', () => {
  let fixture: ComponentFixture<BriefDetailComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BriefDetailComponent] }).compileComponents();
    fixture = TestBed.createComponent(BriefDetailComponent);
  });

  it('heroes the rank-1 item', () => {
    fixture.componentRef.setInput('digest', digest);
    fixture.detectChanges();
    const hero = fixture.nativeElement.querySelector('[data-testid="brief-hero"]');
    expect(hero.textContent).toContain('First');
    expect(hero.querySelector('app-score-badge')).toBeTruthy();
  });

  it('lists the remaining items', () => {
    fixture.componentRef.setInput('digest', digest);
    fixture.detectChanges();
    const rest = fixture.nativeElement.querySelectorAll('[data-testid="brief-item"]');
    expect(rest.length).toBe(1);
    expect(rest[0].textContent).toContain('Second');
  });

  it('shows an empty state when digest is null', () => {
    fixture.componentRef.setInput('digest', null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-empty"]')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/components/brief-detail/brief-detail.component.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the implementation**

```typescript
import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Digest } from '../../models/digest.model';
import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';

@Component({
  selector: 'app-brief-detail',
  standalone: true,
  imports: [DatePipe, ScoreBadgeComponent],
  template: `
    @if (digest(); as d) {
      <article class="brief">
        <header class="brief-head">
          <span class="brief-date">{{ d.date | date: 'fullDate' }}</span>
          <h1>{{ d.title }}</h1>
          <p class="intro">{{ d.intro }}</p>
        </header>

        @if (hero(); as h) {
          <a class="hero" data-testid="brief-hero"
             [class.no-link]="!h.url" [href]="h.url || null" [target]="h.url ? '_blank' : null" rel="noopener">
            <div class="hero-top">
              <span class="rank">#1</span>
              <app-score-badge [score]="h.score" />
            </div>
            <h2>{{ h.title }}</h2>
            <p>{{ h.whyItMatters }}</p>
          </a>
        }

        <ol class="rest">
          @for (item of rest(); track item.ideaId) {
            <li class="brief-item" data-testid="brief-item">
              <span class="rank">#{{ item.rank }}</span>
              <app-score-badge [score]="item.score" />
              <div class="item-main">
                @if (item.url) {
                  <a [href]="item.url" target="_blank" rel="noopener" class="item-title">{{ item.title }}</a>
                } @else {
                  <span class="item-title">{{ item.title }}</span>
                }
                <p class="why">{{ item.whyItMatters }}</p>
              </div>
            </li>
          }
        </ol>
      </article>
    } @else {
      <div class="empty" data-testid="brief-empty">
        <i class="pi pi-inbox"></i>
        <p>No brief selected yet.</p>
      </div>
    }
  `,
  styles: [`
    .brief { padding: 24px 28px; max-width: 760px; }
    .brief-head { margin-bottom: 20px; }
    .brief-date { font-size: 12px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.04em; }
    .brief-head h1 { font-size: 26px; color: var(--text-primary); margin: 6px 0 8px; }
    .intro { color: var(--text-secondary); font-size: 15px; line-height: 1.5; }
    .hero { display: block; text-decoration: none; background: var(--surface-card);
      border: 1px solid var(--surface-border); border-left: 3px solid var(--brand-primary);
      border-radius: var(--r); padding: 18px; margin-bottom: 16px; transition: border-color 0.2s; }
    .hero:not(.no-link):hover { border-color: var(--brand-primary); }
    .hero-top { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .hero .rank { font-weight: 700; color: var(--brand-primary); }
    .hero h2 { font-size: 19px; color: var(--text-primary); margin: 0 0 8px; }
    .hero p { color: var(--text-secondary); margin: 0; line-height: 1.5; }
    .rest { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .brief-item { display: flex; gap: 12px; padding: 12px; border-radius: var(--r-control); align-items: flex-start; }
    .brief-item:hover { background: var(--surface-hover); }
    .brief-item .rank { color: var(--text-muted); font-weight: 600; min-width: 28px; }
    .item-main { flex: 1; min-width: 0; }
    .item-title { color: var(--text-primary); font-weight: 600; text-decoration: none; }
    a.item-title:hover { color: var(--brand-primary); }
    .why { color: var(--text-secondary); font-size: 13px; margin: 4px 0 0; line-height: 1.45; }
    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; height: 100%; color: var(--text-muted); }
    .empty i { font-size: 32px; }
  `],
})
export class BriefDetailComponent {
  readonly digest = input.required<Digest | null>();
  readonly hero = computed(() => this.digest()?.items.find((i) => i.rank === 1) ?? null);
  readonly rest = computed(() => (this.digest()?.items ?? []).filter((i) => i.rank !== 1));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/components/brief-detail/brief-detail.component.spec.ts`
Expected: PASS (3 specs).

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/digest/components/brief-detail
git commit -m "feat(web): BriefDetail component (hero + ranked list)"
```

---

## Task 9: Daily Brief — BriefHistory timeline

**Files:**
- Create: `src/app/features/digest/components/brief-history/brief-history.component.ts`
- Test: `src/app/features/digest/components/brief-history/brief-history.component.spec.ts`

- [ ] **Step 1: Write the failing test**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BriefHistoryComponent } from './brief-history.component';
import { DigestSummary } from '../../models/digest.model';

const items: DigestSummary[] = [
  { id: 'd2', date: '2026-06-06', title: 'Today', itemCount: 8, createdAt: '2026-06-06T07:00:00Z' },
  { id: 'd1', date: '2026-06-05', title: 'Yesterday', itemCount: 6, createdAt: '2026-06-05T07:00:00Z' },
];

describe('BriefHistoryComponent', () => {
  let fixture: ComponentFixture<BriefHistoryComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BriefHistoryComponent] }).compileComponents();
    fixture = TestBed.createComponent(BriefHistoryComponent);
  });

  it('renders one entry per digest', () => {
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd2');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]').length).toBe(2);
  });

  it('marks the selected entry active', () => {
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd1');
    fixture.detectChanges();
    const active = fixture.nativeElement.querySelector('.history-entry.active');
    expect(active.textContent).toContain('Yesterday');
  });

  it('emits select with the id when an entry is clicked', () => {
    let picked: string | undefined;
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd2');
    fixture.componentInstance.select.subscribe((id) => (picked = id));
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]')[1] as HTMLElement).click();
    expect(picked).toBe('d1');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/components/brief-history/brief-history.component.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the implementation**

```typescript
import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DigestSummary } from '../../models/digest.model';

@Component({
  selector: 'app-brief-history',
  standalone: true,
  imports: [DatePipe],
  template: `
    <nav class="history">
      <h2 class="history-title">Daily Briefs</h2>
      @for (d of digests(); track d.id) {
        <button type="button" class="history-entry" [class.active]="d.id === selectedId()"
                data-testid="history-entry" (click)="select.emit(d.id)">
          <span class="entry-date">{{ d.date | date: 'MMM d' }}</span>
          <span class="entry-title">{{ d.title }}</span>
          <span class="entry-count">{{ d.itemCount }} items</span>
        </button>
      }
    </nav>
  `,
  styles: [`
    .history { display: flex; flex-direction: column; padding: 16px 8px; }
    .history-title { font-size: 13px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); padding: 0 8px 8px; margin: 0; }
    .history-entry { text-align: left; background: none; border: none; cursor: pointer;
      display: flex; flex-direction: column; gap: 2px; padding: 10px 12px; border-radius: var(--r-control);
      color: var(--text-secondary); border-left: 2px solid transparent; }
    .history-entry:hover { background: var(--surface-hover); }
    .history-entry.active { background: var(--accent-soft); border-left-color: var(--brand-primary); }
    .entry-date { font-size: 11px; color: var(--text-muted); }
    .entry-title { font-size: 14px; font-weight: 600; color: var(--text-primary); }
    .entry-count { font-size: 11px; color: var(--text-secondary); }
  `],
})
export class BriefHistoryComponent {
  readonly digests = input.required<DigestSummary[]>();
  readonly selectedId = input.required<string | null>();
  readonly select = output<string>();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/components/brief-history/brief-history.component.spec.ts`
Expected: PASS (3 specs).

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/digest/components/brief-history
git commit -m "feat(web): BriefHistory timeline component"
```

---

## Task 10: Daily Brief two-pane shell

**Files:**
- Modify: `src/app/features/digest/pages/daily-brief/daily-brief.component.ts` (full rewrite)
- Test: `src/app/features/digest/pages/daily-brief/daily-brief.component.spec.ts`

- [ ] **Step 1: Rewrite the spec for the two-pane behavior**

```typescript
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DailyBriefComponent } from './daily-brief.component';
import { Digest, DigestSummary } from '../../models/digest.model';

const summaries: DigestSummary[] = [
  { id: 'd2', date: '2026-06-06', title: 'Today', itemCount: 1, createdAt: '2026-06-06T07:00:00Z' },
  { id: 'd1', date: '2026-06-05', title: 'Yesterday', itemCount: 1, createdAt: '2026-06-05T07:00:00Z' },
];
const latest: Digest = { id: 'd2', date: '2026-06-06', title: 'Today', intro: 'i', itemCount: 1,
  createdAt: '2026-06-06T07:00:00Z', items: [{ ideaId: 'a', rank: 1, score: 9, whyItMatters: 'w', title: 'First', url: null }] };

describe('DailyBriefComponent', () => {
  let fixture: ComponentFixture<DailyBriefComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyBriefComponent, HttpClientTestingModule],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DailyBriefComponent);
    fixture.detectChanges(); // ngOnInit
  });

  afterEach(() => http.verify());

  function flushInit() {
    http.expectOne('/api/digests').flush(summaries);
    http.expectOne('/api/digests/latest').flush(latest);
    fixture.detectChanges();
  }

  it('loads history and the latest brief on init', () => {
    flushInit();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="brief-hero"]').textContent).toContain('First');
  });

  it('loads a brief by id when a history entry is selected', () => {
    flushInit();
    fixture.componentInstance.onSelect('d1');
    const req = http.expectOne('/api/digests/d1');
    req.flush({ ...latest, id: 'd1', title: 'Yesterday', items: [{ ideaId: 'b', rank: 1, score: 5, whyItMatters: 'w', title: 'Old', url: null }] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-hero"]').textContent).toContain('Old');
  });

  it('shows empty state when there are no briefs', () => {
    http.expectOne('/api/digests').flush([]);
    http.expectOne('/api/digests/latest').flush(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-empty"]')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/pages/daily-brief/daily-brief.component.spec.ts`
Expected: FAIL — `onSelect` not defined / two-pane markup absent.

- [ ] **Step 3: Rewrite the component**

```typescript
import { Component, OnInit, inject, signal } from '@angular/core';
import { DigestService } from '../../services/digest.service';
import { Digest, DigestSummary } from '../../models/digest.model';
import { BriefHistoryComponent } from '../../components/brief-history/brief-history.component';
import { BriefDetailComponent } from '../../components/brief-detail/brief-detail.component';

@Component({
  selector: 'app-daily-brief',
  standalone: true,
  imports: [BriefHistoryComponent, BriefDetailComponent],
  template: `
    <div class="brief-layout">
      <aside class="history-pane">
        <app-brief-history [digests]="history()" [selectedId]="selectedId()" (select)="onSelect($event)" />
      </aside>
      <main class="detail-pane">
        @if (loading()) {
          <div class="loading">Loading brief…</div>
        } @else {
          <app-brief-detail [digest]="current()" />
        }
      </main>
    </div>
  `,
  styles: [`
    .brief-layout { display: grid; grid-template-columns: 260px 1fr; height: 100%; min-height: 0; }
    .history-pane { border-right: 1px solid var(--surface-border); overflow-y: auto; background: var(--surface-sidebar); }
    .detail-pane { overflow-y: auto; }
    .loading { padding: 48px; text-align: center; color: var(--text-secondary); }
  `],
})
export class DailyBriefComponent implements OnInit {
  private readonly service = inject(DigestService);
  readonly history = signal<DigestSummary[]>([]);
  readonly current = signal<Digest | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly loading = signal(false);

  ngOnInit(): void {
    this.service.list().subscribe({ next: (h) => this.history.set(h), error: () => this.history.set([]) });
    this.service.getLatest().subscribe({
      next: (d) => { this.current.set(d); this.selectedId.set(d?.id ?? null); },
      error: () => this.current.set(null),
    });
  }

  onSelect(id: string): void {
    if (id === this.selectedId()) return;
    this.selectedId.set(id);
    this.loading.set(true);
    this.service.getById(id).subscribe({
      next: (d) => { this.current.set(d); this.loading.set(false); },
      error: () => { this.current.set(null); this.loading.set(false); },
    });
  }
}
```

> Note: `getLatest()` returns `null` (HTTP 200 with null body) when no digests exist; the empty-state test flushes `null`. If the API returns 404 instead, the `error` handler sets `current` to null and the empty state still renders — both paths covered.

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/features/digest/pages/daily-brief/daily-brief.component.spec.ts`
Expected: PASS (3 specs).

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/digest/pages/daily-brief
git commit -m "feat(web): Daily Brief two-pane shell (history + detail)"
```

---

## Task 11: Full verification

- [ ] **Step 1: Run the entire web test suite**

Run: `npx ng test --watch=false --browsers=ChromeHeadless`
Expected: all specs PASS, no errors/warnings.

- [ ] **Step 2: Production build**

Run: `npx ng build --configuration production`
Expected: build succeeds with no errors.

- [ ] **Step 3: Commit any spec fixups**

```bash
git add -A src/PersonalBrandAssistant.Web
git commit -m "test(web): fix specs affected by radar UX redesign"
```

---

## Self-review notes

- **Spec coverage:** Idea Bank tokens+layout (T2,3,6,7), browse/sort/filter chips (T4,6,7), score badges everywhere (T1,2,3,8), score distribution labeled per-page (T5), Daily Brief daily-read hero (T8), history archive (T9,10), loading/empty states (T8,10). All spec sections mapped.
- **Type consistency:** `ScoreBadgeComponent` input is `score: number | null` + `title: string` everywhere; `IdeaFilterState` keys used in chips match the model; `DigestService.list()/getById()/getLatest()` signatures match `digest.service.ts`; `DigestSummary`/`Digest`/`DigestItem` fields match `digest.model.ts`.
- **No backend changes:** confirmed — all digest endpoints already exist.
- **Existing-spec risk:** Tasks 2/3/6/7 rewrite markup, so their existing specs may need selector updates; each task's verify step calls this out. Preserve all `data-testid` values.
