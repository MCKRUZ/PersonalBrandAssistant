diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.spec.ts
new file mode 100644
index 0000000..2145ecb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.spec.ts
@@ -0,0 +1,105 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { provideHttpClient } from '@angular/common/http';
+import { IdeaRankedComponent } from './idea-ranked.component';
+import { IdeaStore } from '../../store/idea.store';
+import { IdeaStatus } from '../../../../models/idea.model';
+import type { Idea } from '../../../../models/idea.model';
+
+function makeIdea(overrides: Partial<Idea>): Idea {
+  return {
+    id: 'idea-' + Math.random().toString(36).slice(2),
+    title: 'Title', description: null, url: null, sourceName: 'Source',
+    category: null, summary: null, thumbnailUrl: null, status: IdeaStatus.New,
+    tags: [], detectedAt: '2026-01-01T00:00:00Z', hasSavedDetails: false,
+    score: 6, scoreReason: null, isDuplicate: false,
+    rank: 0.5, brandFit: 0.5, pillarBreakdown: [], isAntiTopic: null,
+    isAuthorityTopic: null, recencyFactor: 1, stale: false,
+    ...overrides,
+  };
+}
+
+describe('IdeaRankedComponent', () => {
+  let fixture: ComponentFixture<IdeaRankedComponent>;
+  let component: IdeaRankedComponent;
+  let store: InstanceType<typeof IdeaStore>;
+  let el: HTMLElement;
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [IdeaRankedComponent],
+      providers: [provideHttpClient()],
+    });
+    store = TestBed.inject(IdeaStore);
+    fixture = TestBed.createComponent(IdeaRankedComponent);
+    component = fixture.componentInstance;
+    el = fixture.nativeElement as HTMLElement;
+  });
+
+  it('renders a numbered Top-N list ordered as given (highest rank first)', () => {
+    component.ideas = [
+      makeIdea({ id: 'a', title: 'Top', rank: 0.9 }),
+      makeIdea({ id: 'b', title: 'Second', rank: 0.5 }),
+    ];
+    fixture.detectChanges();
+
+    const numerals = el.querySelectorAll('[data-testid="rank-numeral"]');
+    expect(numerals[0].textContent?.trim()).toBe('1');
+    expect(numerals[1].textContent?.trim()).toBe('2');
+    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(2);
+  });
+
+  it('shows each pillar breakdown entry (name + score + reason)', () => {
+    component.ideas = [
+      makeIdea({
+        pillarBreakdown: [{ name: 'Agentic Dev', score: 0.75, reason: 'ownable angle' }],
+      }),
+    ];
+    fixture.detectChanges();
+
+    const breakdown = el.querySelector('[data-testid="pillar-breakdown"]')!;
+    expect(breakdown.textContent).toContain('Agentic Dev');
+    expect(breakdown.textContent).toContain('75%');
+    expect(breakdown.textContent).toContain('ownable angle');
+  });
+
+  it('shows the authority badge only when isAuthorityTopic is true', () => {
+    component.ideas = [makeIdea({ isAuthorityTopic: true })];
+    fixture.detectChanges();
+    expect(el.querySelector('[data-testid="authority-badge"]')).toBeTruthy();
+    expect(el.querySelector('[data-testid="anti-badge"]')).toBeFalsy();
+  });
+
+  it('shows the anti-topic badge when isAntiTopic is true', () => {
+    component.ideas = [makeIdea({ isAntiTopic: true })];
+    fixture.detectChanges();
+    expect(el.querySelector('[data-testid="anti-badge"]')).toBeTruthy();
+  });
+
+  it('shows the stale indicator when stale is true', () => {
+    component.ideas = [makeIdea({ stale: true })];
+    fixture.detectChanges();
+    expect(el.querySelector('[data-testid="stale-badge"]')).toBeTruthy();
+  });
+
+  it('window toggle calls setRankedWindow', () => {
+    const spy = spyOn(store, 'setRankedWindow');
+    component.ideas = [makeIdea({})];
+    fixture.detectChanges();
+
+    (el.querySelector('[data-testid="window-week"] button') as HTMLElement).click();
+    expect(spy).toHaveBeenCalledWith('week');
+
+    (el.querySelector('[data-testid="window-today"] button') as HTMLElement).click();
+    expect(spy).toHaveBeenCalledWith('today');
+  });
+
+  it('renders only the first rankedTopN items', () => {
+    store.setRankedTopN(2);
+    component.ideas = [
+      makeIdea({ id: 'a' }), makeIdea({ id: 'b' }), makeIdea({ id: 'c' }),
+    ];
+    fixture.detectChanges();
+
+    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(2);
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.ts
new file mode 100644
index 0000000..0d08921
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-ranked/idea-ranked.component.ts
@@ -0,0 +1,109 @@
+import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
+import { ButtonModule } from 'primeng/button';
+import { IdeaStore } from '../../store/idea.store';
+import { Idea } from '../../../../models/idea.model';
+import { ScoreBadgeComponent } from '../../../../shared/score-badge/score-badge.component';
+
+@Component({
+  selector: 'app-idea-ranked',
+  standalone: true,
+  imports: [ButtonModule, ScoreBadgeComponent],
+  template: `
+    <div class="idea-ranked">
+      <div class="window-toggle" role="group" aria-label="Ranking window">
+        <p-button label="Today" size="small"
+          [severity]="store.rankedWindow() === 'today' ? 'primary' : 'secondary'"
+          [text]="store.rankedWindow() !== 'today'"
+          (onClick)="store.setRankedWindow('today')" data-testid="window-today" />
+        <p-button label="This week" size="small"
+          [severity]="store.rankedWindow() === 'week' ? 'primary' : 'secondary'"
+          [text]="store.rankedWindow() !== 'week'"
+          (onClick)="store.setRankedWindow('week')" data-testid="window-week" />
+      </div>
+
+      @if (ideas.length === 0) {
+        <div class="empty" data-testid="ranked-empty">No ranked ideas in this window yet.</div>
+      }
+
+      <ol class="ranked-list">
+        @for (idea of ideas.slice(0, store.rankedTopN()); track idea.id; let i = $index) {
+          <li class="ranked-item" data-testid="ranked-item">
+            <div class="rank-numeral" data-testid="rank-numeral">{{ i + 1 }}</div>
+            <div class="ranked-body">
+              <div class="ranked-head">
+                <h3 class="ranked-title">{{ idea.title }}</h3>
+                <app-score-badge [score]="idea.score" />
+                @if (idea.isAuthorityTopic) {
+                  <span class="badge authority" data-testid="authority-badge">Authority</span>
+                }
+                @if (idea.isAntiTopic) {
+                  <span class="badge anti" data-testid="anti-badge">Anti-topic</span>
+                }
+                @if (idea.stale) {
+                  <span class="badge stale" data-testid="stale-badge">Stale</span>
+                }
+              </div>
+              <div class="ranked-meta">{{ idea.sourceName }}</div>
+
+              @if (idea.pillarBreakdown.length > 0) {
+                <ul class="breakdown" data-testid="pillar-breakdown">
+                  @for (pillar of idea.pillarBreakdown; track pillar.name) {
+                    <li class="breakdown-row">
+                      <span class="pillar-name">{{ pillar.name }}</span>
+                      <span class="pillar-score">{{ pct(pillar.score) }}%</span>
+                      <span class="pillar-reason">{{ pillar.reason }}</span>
+                    </li>
+                  }
+                </ul>
+              }
+
+              <div class="ranked-actions">
+                <p-button label="Save" size="small" [text]="true" (onClick)="save.emit(idea.id)" />
+                <p-button label="Dismiss" size="small" [text]="true" severity="secondary"
+                  (onClick)="dismiss.emit(idea.id)" />
+                <p-button label="Create content" size="small" [text]="true"
+                  (onClick)="createContent.emit(idea.id)" />
+              </div>
+            </div>
+          </li>
+        }
+      </ol>
+    </div>
+  `,
+  styles: [
+    `
+      .window-toggle { display: flex; gap: 4px; margin-bottom: 16px; }
+      .ranked-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 12px; }
+      .ranked-item { display: flex; gap: 16px; align-items: flex-start;
+        padding: 12px; border: 1px solid var(--surface-border); border-radius: var(--r-card, 8px); }
+      .rank-numeral { font-size: 32px; font-weight: 700; line-height: 1; color: var(--brand-primary);
+        min-width: 48px; text-align: center; }
+      .ranked-body { flex: 1; min-width: 0; }
+      .ranked-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
+      .ranked-title { font-size: 15px; font-weight: 600; margin: 0; color: var(--text-primary); }
+      .ranked-meta { font-size: 12px; color: var(--text-secondary); margin: 2px 0 8px; }
+      .badge { font-size: 10px; font-weight: 700; padding: 2px 6px; border-radius: var(--r-pill, 999px); }
+      .badge.authority { background: color-mix(in srgb, var(--score-success) 18%, transparent); color: var(--score-success); }
+      .badge.anti { background: color-mix(in srgb, var(--score-danger) 18%, transparent); color: var(--score-danger); }
+      .badge.stale { background: color-mix(in srgb, var(--score-warning) 18%, transparent); color: var(--score-warning); }
+      .breakdown { list-style: none; margin: 0 0 8px; padding: 0; display: flex; flex-direction: column; gap: 2px; }
+      .breakdown-row { display: flex; gap: 8px; font-size: 12px; }
+      .pillar-name { font-weight: 600; color: var(--text-primary); }
+      .pillar-score { color: var(--brand-primary); }
+      .pillar-reason { color: var(--text-secondary); }
+      .ranked-actions { display: flex; gap: 4px; }
+      .empty { padding: 32px; text-align: center; color: var(--text-secondary); }
+    `,
+  ],
+})
+export class IdeaRankedComponent {
+  readonly store = inject(IdeaStore);
+  @Input({ required: true }) ideas: Idea[] = [];
+  @Output() save = new EventEmitter<string>();
+  @Output() dismiss = new EventEmitter<string>();
+  @Output() createContent = new EventEmitter<string>();
+
+  pct(score: number): number {
+    return Math.round(score * 100);
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.spec.ts
index 4a4e506..fc1e5c4 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.spec.ts
@@ -36,4 +36,16 @@ describe('ViewToggleComponent', () => {
     fixture.detectChanges();
     expect(store.viewMode()).toBe('grid');
   });
+
+  it('renders a ranked toggle button', () => {
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.querySelector('[data-testid="ranked-toggle"]')).toBeTruthy();
+  });
+
+  it('switches to ranked mode on ranked button click', () => {
+    const rankedBtn = fixture.nativeElement.querySelector('[data-testid="ranked-toggle"] button') as HTMLElement;
+    rankedBtn.click();
+    fixture.detectChanges();
+    expect(store.viewMode()).toBe('ranked');
+  });
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.ts
index 2764b6f..d988015 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/view-toggle/view-toggle.component.ts
@@ -12,16 +12,23 @@ import { ButtonModule } from 'primeng/button';
         [icon]="'pi pi-th-large'"
         [severity]="store.viewMode() === 'grid' ? 'primary' : 'secondary'"
         [text]="store.viewMode() !== 'grid'"
-        (onClick)="store.viewMode() !== 'grid' && store.toggleView()"
+        (onClick)="store.setViewMode('grid')"
         size="small"
         data-testid="grid-toggle" />
       <p-button
         [icon]="'pi pi-list'"
         [severity]="store.viewMode() === 'list' ? 'primary' : 'secondary'"
         [text]="store.viewMode() !== 'list'"
-        (onClick)="store.viewMode() !== 'list' && store.toggleView()"
+        (onClick)="store.setViewMode('list')"
         size="small"
         data-testid="list-toggle" />
+      <p-button
+        [icon]="'pi pi-sort-amount-down'"
+        [severity]="store.viewMode() === 'ranked' ? 'primary' : 'secondary'"
+        [text]="store.viewMode() !== 'ranked'"
+        (onClick)="store.setViewMode('ranked')"
+        size="small"
+        data-testid="ranked-toggle" />
     </div>
   `,
   styles: [
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.component.ts
index f90e298..12364bc 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.component.ts
@@ -11,6 +11,7 @@ import { IdeaFilterSidebarComponent } from './components/idea-filter-sidebar/ide
 import { ViewToggleComponent } from './components/view-toggle/view-toggle.component';
 import { IdeaGridComponent } from './components/idea-grid/idea-grid.component';
 import { IdeaListComponent } from './components/idea-list/idea-list.component';
+import { IdeaRankedComponent } from './components/idea-ranked/idea-ranked.component';
 import { SaveIdeaDialogComponent } from './components/save-idea-dialog/save-idea-dialog.component';
 import { SmartSuggestionsComponent } from './components/smart-suggestions/smart-suggestions.component';
 import { ActiveFilterChipsComponent } from './components/active-filter-chips/active-filter-chips.component';
@@ -33,6 +34,7 @@ import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
     ViewToggleComponent,
     IdeaGridComponent,
     IdeaListComponent,
+    IdeaRankedComponent,
     SaveIdeaDialogComponent,
     SmartSuggestionsComponent,
     ActiveFilterChipsComponent,
@@ -78,6 +80,12 @@ import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
               (save)="onSave($event)"
               (dismiss)="onDismiss($event)"
               (createContent)="onCreateContent($event)" />
+          } @else if (store.viewMode() === 'ranked') {
+            <app-idea-ranked
+              [ideas]="store.ideas()"
+              (save)="onSave($event)"
+              (dismiss)="onDismiss($event)"
+              (createContent)="onCreateContent($event)" />
           } @else {
             <app-idea-list
               [ideas]="store.ideas()"
@@ -204,11 +212,12 @@ export class IdeasComponent implements OnInit {
   private searchTimer: ReturnType<typeof setTimeout> | null = null;
 
   readonly sortOptions = [
+    { label: 'Best fit', value: 'rank' },
     { label: 'Newest', value: 'detectedAt' },
     { label: 'Highest score', value: 'score' },
     { label: 'Source', value: 'sourceName' },
   ];
-  sortField = 'detectedAt';
+  sortField = 'rank';
 
   ngOnInit(): void {
     this.store.loadIdeas();
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.ts
index b8a7b99..1bca584 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.ts
@@ -13,7 +13,9 @@ type IdeaStoreState = {
   pageSize: number;
   filter: IdeaFilterState;
   sort: IdeaSortState;
-  viewMode: 'grid' | 'list';
+  viewMode: 'grid' | 'list' | 'ranked';
+  rankedWindow: 'today' | 'week';
+  rankedTopN: number;
   selectedIdeaId: string | null;
   loading: boolean;
   error: string | null;
@@ -34,8 +36,10 @@ const initialState: IdeaStoreState = {
     searchText: null,
     minScore: null,
   },
-  sort: { field: 'detectedAt', direction: 'desc' },
+  sort: { field: 'rank', direction: 'desc' },
   viewMode: 'list',
+  rankedWindow: 'today',
+  rankedTopN: 20,
   selectedIdeaId: null,
   loading: false,
   error: null,
@@ -89,10 +93,27 @@ export const IdeaStore = signalStore(
         patchState(store, { page });
         loadIdeas();
       },
-      toggleView(): void {
+      setViewMode(mode: 'grid' | 'list' | 'ranked'): void {
+        patchState(store, { viewMode: mode });
+      },
+      setRankedWindow(window: 'today' | 'week'): void {
+        patchState(store, { rankedWindow: window });
+        // Apply the window as a dateFrom filter (Today = local midnight; This week = now − 7 days). Goes
+        // through setFilter's pipeline so page resets to 1 and the list reloads.
+        const from =
+          window === 'today'
+            ? new Date(new Date().setHours(0, 0, 0, 0))
+            : new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);
         patchState(store, {
-          viewMode: store.viewMode() === 'list' ? 'grid' : 'list',
+          filter: { ...store.filter(), dateFrom: from.toISOString() },
+          page: 1,
         });
+        loadIdeas();
+      },
+      setRankedTopN(n: number): void {
+        // pageSize tracks N so the single numbered page returns at least N ideas from the server.
+        patchState(store, { rankedTopN: n, pageSize: n, page: 1 });
+        loadIdeas();
       },
       selectIdea(id: string | null): void {
         patchState(store, { selectedIdeaId: id });
diff --git a/src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts b/src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts
index 5144776..18dafd3 100644
--- a/src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/models/idea.model.ts
@@ -14,6 +14,12 @@ export enum IdeaSourceType {
   AIGenerated = 'AIGenerated',
 }
 
+export interface PillarBreakdown {
+  name: string;
+  score: number;
+  reason: string;
+}
+
 export interface Idea {
   id: string;
   title: string;
@@ -30,6 +36,15 @@ export interface Idea {
   score: number | null;
   scoreReason: string | null;
   isDuplicate: boolean;
+
+  // Brand-anchored ranking (section-08 IdeaDto), always returned by the backend.
+  rank: number;
+  brandFit: number;
+  pillarBreakdown: PillarBreakdown[];
+  isAntiTopic: boolean | null;
+  isAuthorityTopic: boolean | null;
+  recencyFactor: number;
+  stale: boolean;
 }
 
 export interface IdeaDetail extends Idea {
