diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.spec.ts
new file mode 100644
index 0000000..136ea4c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.spec.ts
@@ -0,0 +1,118 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { of } from 'rxjs';
+import { FeedBatchToolbarComponent } from './feed-batch-toolbar.component';
+import { FeedStore } from '../store/feed.store';
+import { FeedService } from '../services/feed.service';
+import { FeedHubService } from '../services/feed-hub.service';
+import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
+import type { FeedItem } from '../models/feed-item.model';
+import type { FeedSummary } from '../models/feed-summary.model';
+import type { PagedResult } from '../../../models/pagination.model';
+
+describe('FeedBatchToolbarComponent', () => {
+  let fixture: ComponentFixture<FeedBatchToolbarComponent>;
+  let store: InstanceType<typeof FeedStore>;
+
+  const mockItem = (id: string): FeedItem => ({
+    id, type: FeedItemType.TrendAlert, title: 'Test',
+    summary: 'Test', data: null, actionType: null, actionTargetId: null,
+    priority: FeedItemPriority.Normal, isRead: false, isActedOn: false,
+    createdAt: '2026-01-01T00:00:00Z', expiresAt: null,
+  });
+
+  const mockSummary: FeedSummary = {
+    unreadCount: 5, pendingApprovals: 2, trendingCount: 3, engagementDelta: 1.5,
+  };
+
+  beforeEach(() => {
+    const feedService = jasmine.createSpyObj('FeedService', [
+      'list', 'getSummary', 'getTrending',
+      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
+    ]);
+    const mockPage: PagedResult<FeedItem> = {
+      items: [mockItem('item-1'), mockItem('item-2'), mockItem('item-3')],
+      totalCount: 3, page: 1, pageSize: 20, totalPages: 1,
+    };
+    feedService.list.and.returnValue(of(mockPage));
+    feedService.getSummary.and.returnValue(of(mockSummary));
+    feedService.getTrending.and.returnValue(of([]));
+    feedService.batchAct.and.returnValue(of({ failures: [] }));
+    feedService.batchMarkRead.and.returnValue(of(undefined));
+
+    TestBed.configureTestingModule({
+      imports: [FeedBatchToolbarComponent],
+      providers: [
+        FeedStore,
+        { provide: FeedService, useValue: feedService },
+        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
+      ],
+    });
+
+    store = TestBed.inject(FeedStore);
+    fixture = TestBed.createComponent(FeedBatchToolbarComponent);
+    fixture.detectChanges();
+  });
+
+  it('hides toolbar when store.hasSelection is false', () => {
+    const toolbar = fixture.nativeElement.querySelector('.batch-toolbar');
+    expect(toolbar).toBeFalsy();
+  });
+
+  it('shows toolbar when store.hasSelection is true', () => {
+    store.toggleSelect('item-1');
+    fixture.detectChanges();
+
+    const toolbar = fixture.nativeElement.querySelector('.batch-toolbar');
+    expect(toolbar).toBeTruthy();
+  });
+
+  it('displays selected count', () => {
+    store.toggleSelect('item-1');
+    store.toggleSelect('item-2');
+    fixture.detectChanges();
+
+    const count = fixture.nativeElement.querySelector('[data-testid="selected-count"]');
+    expect(count.textContent).toContain('2');
+  });
+
+  it('Approve button calls store.batchAct with selected IDs and approve', () => {
+    store.toggleSelect('item-1');
+    store.toggleSelect('item-2');
+    fixture.detectChanges();
+
+    spyOn(store, 'batchAct');
+    const btn = fixture.nativeElement.querySelector('[data-testid="btn-approve"]');
+    btn.click();
+    expect(store.batchAct).toHaveBeenCalledWith(['item-1', 'item-2'], 'approve');
+  });
+
+  it('Dismiss button calls store.batchAct with selected IDs and dismiss', () => {
+    store.toggleSelect('item-1');
+    fixture.detectChanges();
+
+    spyOn(store, 'batchAct');
+    const btn = fixture.nativeElement.querySelector('[data-testid="btn-dismiss"]');
+    btn.click();
+    expect(store.batchAct).toHaveBeenCalledWith(['item-1'], 'dismiss');
+  });
+
+  it('Mark Read button calls store.batchMarkRead', () => {
+    store.toggleSelect('item-1');
+    fixture.detectChanges();
+
+    spyOn(store, 'batchMarkRead');
+    const btn = fixture.nativeElement.querySelector('[data-testid="btn-mark-read"]');
+    btn.click();
+    expect(store.batchMarkRead).toHaveBeenCalled();
+  });
+
+  it('Clear button calls store.clearSelection', () => {
+    store.toggleSelect('item-1');
+    fixture.detectChanges();
+
+    spyOn(store, 'clearSelection');
+    const btn = fixture.nativeElement.querySelector('[data-testid="btn-clear"]');
+    btn.click();
+    expect(store.clearSelection).toHaveBeenCalled();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.ts
new file mode 100644
index 0000000..af77c5d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.ts
@@ -0,0 +1,119 @@
+import { Component, inject } from '@angular/core';
+import { FeedStore } from '../store/feed.store';
+
+@Component({
+  selector: 'app-feed-batch-toolbar',
+  standalone: true,
+  template: `
+    @if (store.hasSelection()) {
+      <div class="batch-toolbar" data-testid="batch-toolbar">
+        <span class="selected-count" data-testid="selected-count">
+          {{ store.selectedCount() }} selected
+        </span>
+
+        <div class="actions">
+          <button class="btn btn-success" data-testid="btn-approve"
+            (click)="approve()">
+            Approve
+          </button>
+          <button class="btn btn-info" data-testid="btn-mark-read"
+            (click)="markRead()">
+            Mark Read
+          </button>
+          <button class="btn btn-secondary" data-testid="btn-dismiss"
+            (click)="dismiss()">
+            Dismiss
+          </button>
+          <button class="btn btn-text" data-testid="btn-clear"
+            (click)="store.clearSelection()">
+            Clear
+          </button>
+        </div>
+      </div>
+    }
+  `,
+  styles: [`
+    .batch-toolbar {
+      display: flex;
+      align-items: center;
+      justify-content: space-between;
+      background: #161b22;
+      border: 1px solid #30363d;
+      border-radius: 8px;
+      padding: 12px 16px;
+    }
+
+    .selected-count {
+      color: #f0f6fc;
+      font-size: 14px;
+      font-weight: 500;
+    }
+
+    .actions {
+      display: flex;
+      gap: 8px;
+    }
+
+    .btn {
+      border: none;
+      border-radius: 6px;
+      cursor: pointer;
+      font-size: 13px;
+      font-weight: 500;
+      padding: 6px 14px;
+      transition: opacity 0.2s;
+    }
+
+    .btn:hover { opacity: 0.85; }
+
+    .btn-success {
+      background: #238636;
+      color: #f0f6fc;
+    }
+
+    .btn-info {
+      background: #1f6feb;
+      color: #f0f6fc;
+    }
+
+    .btn-secondary {
+      background: #30363d;
+      color: #c9d1d9;
+    }
+
+    .btn-text {
+      background: transparent;
+      color: #8b949e;
+    }
+
+    .btn-text:hover {
+      color: #f0f6fc;
+    }
+
+    @media (max-width: 480px) {
+      .batch-toolbar {
+        flex-direction: column;
+        gap: 12px;
+      }
+      .actions {
+        flex-wrap: wrap;
+        justify-content: center;
+      }
+    }
+  `],
+})
+export class FeedBatchToolbarComponent {
+  protected readonly store = inject(FeedStore);
+
+  approve(): void {
+    this.store.batchAct(this.store.selectedIds(), 'approve');
+  }
+
+  dismiss(): void {
+    this.store.batchAct(this.store.selectedIds(), 'dismiss');
+  }
+
+  markRead(): void {
+    this.store.batchMarkRead();
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.spec.ts
new file mode 100644
index 0000000..65e3d77
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.spec.ts
@@ -0,0 +1,168 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { of } from 'rxjs';
+import { ActivatedRoute, Router } from '@angular/router';
+import { FeedFilterTabsComponent } from './feed-filter-tabs.component';
+import { FeedStore } from '../store/feed.store';
+import { FeedService } from '../services/feed.service';
+import { FeedHubService } from '../services/feed-hub.service';
+import { FeedItemType } from '../models/feed-item.model';
+import type { FeedSummary } from '../models/feed-summary.model';
+import type { FeedItem } from '../models/feed-item.model';
+import type { PagedResult } from '../../../models/pagination.model';
+
+describe('FeedFilterTabsComponent', () => {
+  let fixture: ComponentFixture<FeedFilterTabsComponent>;
+  let store: InstanceType<typeof FeedStore>;
+  let router: jasmine.SpyObj<Router>;
+
+  const emptyPage: PagedResult<FeedItem> = {
+    items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
+  };
+
+  const mockSummary: FeedSummary = {
+    unreadCount: 12, pendingApprovals: 3, trendingCount: 7, engagementDelta: 4.2,
+  };
+
+  function setup(queryParams: Record<string, string> = {}): void {
+    const feedService = jasmine.createSpyObj('FeedService', [
+      'list', 'getSummary', 'getTrending',
+      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
+    ]);
+    feedService.list.and.returnValue(of(emptyPage));
+    feedService.getSummary.and.returnValue(of(mockSummary));
+    feedService.getTrending.and.returnValue(of([]));
+
+    router = jasmine.createSpyObj('Router', ['navigate']);
+
+    TestBed.configureTestingModule({
+      imports: [FeedFilterTabsComponent],
+      providers: [
+        FeedStore,
+        { provide: FeedService, useValue: feedService },
+        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
+        { provide: Router, useValue: router },
+        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
+      ],
+    });
+
+    store = TestBed.inject(FeedStore);
+    fixture = TestBed.createComponent(FeedFilterTabsComponent);
+    fixture.detectChanges();
+  }
+
+  beforeEach(() => setup());
+
+  it('renders All, Drafts, Trends, Ideas, Analytics, Approvals tabs', () => {
+    const tabs = fixture.nativeElement.querySelectorAll('.tab');
+    expect(tabs.length).toBe(6);
+    expect(tabs[0].textContent).toContain('All');
+    expect(tabs[1].textContent).toContain('Drafts');
+    expect(tabs[2].textContent).toContain('Trends');
+    expect(tabs[3].textContent).toContain('Ideas');
+    expect(tabs[4].textContent).toContain('Analytics');
+    expect(tabs[5].textContent).toContain('Approvals');
+  });
+
+  it('highlights active tab based on store.activeFilter', () => {
+    store.setFilter(FeedItemType.TrendAlert);
+    fixture.detectChanges();
+
+    const activeTab = fixture.nativeElement.querySelector('.tab.active');
+    expect(activeTab).toBeTruthy();
+    expect(activeTab.textContent).toContain('Trends');
+  });
+
+  it('click on tab calls store.setFilter with correct type', () => {
+    spyOn(store, 'setFilter');
+    const trendsTab = fixture.nativeElement.querySelector('[data-testid="tab-trends"]');
+    trendsTab.click();
+    expect(store.setFilter).toHaveBeenCalledWith(FeedItemType.TrendAlert);
+  });
+
+  it('"All" tab passes null to setFilter', () => {
+    store.setFilter(FeedItemType.TrendAlert);
+    fixture.detectChanges();
+
+    spyOn(store, 'setFilter');
+    const allTab = fixture.nativeElement.querySelector('[data-testid="tab-all"]');
+    allTab.click();
+    expect(store.setFilter).toHaveBeenCalledWith(null);
+  });
+
+  it('shows unread count badge on All tab', () => {
+    const badge = fixture.nativeElement.querySelector('[data-testid="tab-all"] .badge');
+    expect(badge).toBeTruthy();
+    expect(badge.textContent.trim()).toBe('12');
+  });
+
+  it('shows trending count badge on Trends tab', () => {
+    const badge = fixture.nativeElement.querySelector('[data-testid="tab-trends"] .badge');
+    expect(badge).toBeTruthy();
+    expect(badge.textContent.trim()).toBe('7');
+  });
+
+  it('shows pending approvals badge on Approvals tab', () => {
+    const badge = fixture.nativeElement.querySelector('[data-testid="tab-approvals"] .badge');
+    expect(badge).toBeTruthy();
+    expect(badge.textContent.trim()).toBe('3');
+  });
+
+  it('does not show badge on tabs without counts', () => {
+    const draftsBadge = fixture.nativeElement.querySelector('[data-testid="tab-drafts"] .badge');
+    const ideasBadge = fixture.nativeElement.querySelector('[data-testid="tab-ideas"] .badge');
+    const analyticsBadge = fixture.nativeElement.querySelector('[data-testid="tab-analytics"] .badge');
+    expect(draftsBadge).toBeFalsy();
+    expect(ideasBadge).toBeFalsy();
+    expect(analyticsBadge).toBeFalsy();
+  });
+
+  it('updates URL query params on tab change', () => {
+    const trendsTab = fixture.nativeElement.querySelector('[data-testid="tab-trends"]');
+    trendsTab.click();
+    expect(router.navigate).toHaveBeenCalledWith([], {
+      queryParams: { type: FeedItemType.TrendAlert },
+      queryParamsHandling: 'merge',
+    });
+  });
+
+  it('removes type query param when All tab selected', () => {
+    const allTab = fixture.nativeElement.querySelector('[data-testid="tab-all"]');
+    allTab.click();
+    expect(router.navigate).toHaveBeenCalledWith([], {
+      queryParams: { type: null },
+      queryParamsHandling: 'merge',
+    });
+  });
+});
+
+describe('FeedFilterTabsComponent (URL init)', () => {
+  it('reads initial filter from URL query params', () => {
+    const feedService = jasmine.createSpyObj('FeedService', [
+      'list', 'getSummary', 'getTrending',
+      'markRead', 'actOnItem', 'batchMarkRead', 'batchDismiss', 'batchAct',
+    ]);
+    feedService.list.and.returnValue(of({
+      items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0,
+    }));
+    feedService.getSummary.and.returnValue(of({
+      unreadCount: 0, pendingApprovals: 0, trendingCount: 0, engagementDelta: 0,
+    }));
+    feedService.getTrending.and.returnValue(of([]));
+
+    TestBed.configureTestingModule({
+      imports: [FeedFilterTabsComponent],
+      providers: [
+        FeedStore,
+        { provide: FeedService, useValue: feedService },
+        { provide: FeedHubService, useValue: { feedItemReceived$: of(), summaryUpdated$: of() } },
+        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
+        { provide: ActivatedRoute, useValue: { queryParams: of({ type: 'TrendAlert' }) } },
+      ],
+    });
+
+    const store = TestBed.inject(FeedStore);
+    TestBed.createComponent(FeedFilterTabsComponent).detectChanges();
+
+    expect(store.activeFilter()).toBe(FeedItemType.TrendAlert);
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.ts
new file mode 100644
index 0000000..6c54565
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.ts
@@ -0,0 +1,132 @@
+import { Component, inject } from '@angular/core';
+import { ActivatedRoute, Router } from '@angular/router';
+import { take } from 'rxjs';
+import { FeedStore } from '../store/feed.store';
+import { FeedItemType } from '../models/feed-item.model';
+
+interface TabConfig {
+  label: string;
+  value: FeedItemType | null;
+  testId: string;
+}
+
+@Component({
+  selector: 'app-feed-filter-tabs',
+  standalone: true,
+  template: `
+    <div class="filter-tabs" role="tablist">
+      @for (tab of tabs; track tab.testId) {
+        <button
+          class="tab"
+          [class.active]="store.activeFilter() === tab.value"
+          [attr.data-testid]="'tab-' + tab.testId"
+          [attr.aria-selected]="store.activeFilter() === tab.value"
+          role="tab"
+          (click)="selectTab(tab.value)">
+          {{ tab.label }}
+          @if (getBadge(tab.value); as badge) {
+            <span class="badge">{{ badge }}</span>
+          }
+        </button>
+      }
+    </div>
+  `,
+  styles: [`
+    .filter-tabs {
+      display: flex;
+      gap: 4px;
+      border-bottom: 1px solid #30363d;
+      padding-bottom: 0;
+    }
+
+    .tab {
+      background: none;
+      border: none;
+      border-bottom: 2px solid transparent;
+      color: #8b949e;
+      cursor: pointer;
+      font-size: 14px;
+      padding: 8px 16px;
+      transition: color 0.2s, border-color 0.2s;
+      display: flex;
+      align-items: center;
+      gap: 8px;
+      white-space: nowrap;
+    }
+
+    .tab:hover {
+      color: #f0f6fc;
+    }
+
+    .tab.active {
+      color: #f0f6fc;
+      border-bottom-color: #58a6ff;
+    }
+
+    .badge {
+      background: #30363d;
+      color: #8b949e;
+      font-size: 11px;
+      font-weight: 600;
+      padding: 1px 6px;
+      border-radius: 10px;
+      min-width: 18px;
+      text-align: center;
+    }
+
+    .tab.active .badge {
+      background: #1f6feb;
+      color: #f0f6fc;
+    }
+
+    @media (max-width: 768px) {
+      .filter-tabs {
+        overflow-x: auto;
+        scrollbar-width: none;
+      }
+      .filter-tabs::-webkit-scrollbar {
+        display: none;
+      }
+    }
+  `]
+})
+export class FeedFilterTabsComponent {
+  protected readonly store = inject(FeedStore);
+  private readonly route = inject(ActivatedRoute);
+  private readonly router = inject(Router);
+
+  readonly tabs: readonly TabConfig[] = [
+    { label: 'All', value: null, testId: 'all' },
+    { label: 'Drafts', value: FeedItemType.AgentDraft, testId: 'drafts' },
+    { label: 'Trends', value: FeedItemType.TrendAlert, testId: 'trends' },
+    { label: 'Ideas', value: FeedItemType.IdeaSuggestion, testId: 'ideas' },
+    { label: 'Analytics', value: FeedItemType.AnalyticsHighlight, testId: 'analytics' },
+    { label: 'Approvals', value: FeedItemType.ApprovalRequest, testId: 'approvals' },
+  ];
+
+  constructor() {
+    this.route.queryParams.pipe(take(1)).subscribe(params => {
+      const type = params['type'];
+      if (type && Object.values(FeedItemType).includes(type as FeedItemType)) {
+        this.store.setFilter(type as FeedItemType);
+      }
+    });
+  }
+
+  selectTab(type: FeedItemType | null): void {
+    this.store.setFilter(type);
+    this.router.navigate([], {
+      queryParams: { type },
+      queryParamsHandling: 'merge',
+    });
+  }
+
+  protected getBadge(type: FeedItemType | null): number {
+    const summary = this.store.summary();
+    if (!summary) return 0;
+    if (type === null) return summary.unreadCount;
+    if (type === FeedItemType.TrendAlert) return summary.trendingCount;
+    if (type === FeedItemType.ApprovalRequest) return summary.pendingApprovals;
+    return 0;
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.spec.ts
index c0d2998..6d2e017 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.spec.ts
@@ -1,5 +1,6 @@
 import { ComponentFixture, TestBed } from '@angular/core/testing';
 import { of, Subject } from 'rxjs';
+import { ActivatedRoute, Router } from '@angular/router';
 import { FeedPageComponent } from './feed-page.component';
 import { FeedStore } from '../store/feed.store';
 import { FeedService } from '../services/feed.service';
@@ -54,6 +55,8 @@ describe('FeedPageComponent', () => {
             summaryUpdated$: of(),
           },
         },
+        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
+        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
       ],
     }).compileComponents();
 
@@ -68,9 +71,9 @@ describe('FeedPageComponent', () => {
     expect(statsBar).toBeTruthy();
   });
 
-  it('renders filter tabs placeholder', () => {
-    const placeholder = fixture.nativeElement.querySelector('[data-testid="filter-tabs-slot"]');
-    expect(placeholder).toBeTruthy();
+  it('renders FeedFilterTabs component', () => {
+    const filterTabs = fixture.nativeElement.querySelector('app-feed-filter-tabs');
+    expect(filterTabs).toBeTruthy();
   });
 
   it('renders card list placeholder', () => {
@@ -83,16 +86,21 @@ describe('FeedPageComponent', () => {
     expect(placeholder).toBeTruthy();
   });
 
-  it('hides batch toolbar when no items selected', () => {
-    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar-slot"]');
+  it('renders FeedBatchToolbar component', () => {
+    const toolbar = fixture.nativeElement.querySelector('app-feed-batch-toolbar');
+    expect(toolbar).toBeTruthy();
+  });
+
+  it('hides batch toolbar content when no items selected', () => {
+    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar"]');
     expect(toolbar).toBeFalsy();
   });
 
-  it('renders batch toolbar when items are selected', () => {
+  it('renders batch toolbar content when items are selected', () => {
     store.toggleSelect('feed-1');
     fixture.detectChanges();
 
-    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar-slot"]');
+    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar"]');
     expect(toolbar).toBeTruthy();
   });
 
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts
index 648b1d6..31d8f7f 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts
@@ -1,11 +1,13 @@
 import { Component, computed, inject } from '@angular/core';
 import { FeedStatsBarComponent } from '../feed-stats-bar/feed-stats-bar.component';
+import { FeedFilterTabsComponent } from '../feed-filter-tabs/feed-filter-tabs.component';
+import { FeedBatchToolbarComponent } from '../feed-batch-toolbar/feed-batch-toolbar.component';
 import { FeedStore } from '../store/feed.store';
 
 @Component({
   selector: 'app-feed-page',
   standalone: true,
-  imports: [FeedStatsBarComponent],
+  imports: [FeedStatsBarComponent, FeedFilterTabsComponent, FeedBatchToolbarComponent],
   template: `
     <div class="page">
       <h1>Feed</h1>
@@ -15,11 +17,9 @@ import { FeedStore } from '../store/feed.store';
 
       <div class="feed-grid">
         <div class="feed-main">
-          <div data-testid="filter-tabs-slot" class="placeholder">Filter Tabs</div>
+          <app-feed-filter-tabs />
 
-          @if (store.hasSelection()) {
-            <div data-testid="batch-toolbar-slot" class="placeholder">Batch Toolbar</div>
-          }
+          <app-feed-batch-toolbar />
 
           @if (store.newItemCount() > 0) {
             <div data-testid="new-items-banner-slot" class="placeholder">
