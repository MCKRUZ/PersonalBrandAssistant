diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts
new file mode 100644
index 0000000..2022fcb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts
@@ -0,0 +1,123 @@
+import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
+import { signal } from '@angular/core';
+import { NoopAnimationsModule } from '@angular/platform-browser/animations';
+import { provideRouter } from '@angular/router';
+import { provideHttpClient } from '@angular/common/http';
+import { AnalyticsDashboardComponent } from './analytics-dashboard.component';
+import { AnalyticsStore } from './store/analytics.store';
+import { AnalyticsService } from './services/analytics.service';
+import { DashboardSummary, DashboardPeriod } from './models/dashboard.model';
+
+describe('AnalyticsDashboardComponent', () => {
+  let component: AnalyticsDashboardComponent;
+  let fixture: ComponentFixture<AnalyticsDashboardComponent>;
+
+  const mockSummary: DashboardSummary = {
+    totalEngagement: 500,
+    previousEngagement: 400,
+    totalImpressions: 10000,
+    previousImpressions: 8000,
+    engagementRate: 5.0,
+    previousEngagementRate: 4.0,
+    contentPublished: 10,
+    previousContentPublished: 8,
+    costPerEngagement: 0.02,
+    previousCostPerEngagement: 0.03,
+    websiteUsers: 1200,
+    previousWebsiteUsers: 1000,
+    generatedAt: '2026-03-25T00:00:00Z',
+  };
+
+  const loadDashboardSpy = jasmine.createSpy('loadDashboard');
+  const refreshDashboardSpy = jasmine.createSpy('refreshDashboard');
+  const setPeriodSpy = jasmine.createSpy('setPeriod');
+
+  const summarySignal = signal<DashboardSummary | null>(mockSummary);
+  const loadingSignal = signal(false);
+  const periodSignal = signal<DashboardPeriod>('30d');
+  const lastRefreshedSignal = signal<string | null>(new Date().toISOString());
+  const isStaleSignal = signal(false);
+  const topContentSignal = signal<readonly any[]>([]);
+  const timelineSignal = signal<readonly any[]>([]);
+  const platformSummariesSignal = signal<readonly any[]>([]);
+  const errorsSignal = signal({ summary: null, timeline: null, platforms: null, website: null, substack: null, topContent: null });
+
+  const mockStore = {
+    summary: summarySignal,
+    loading: loadingSignal,
+    period: periodSignal,
+    lastRefreshedAt: lastRefreshedSignal,
+    isStale: isStaleSignal,
+    topContent: topContentSignal,
+    timeline: timelineSignal,
+    platformSummaries: platformSummariesSignal,
+    errors: errorsSignal,
+    loadDashboard: loadDashboardSpy,
+    refreshDashboard: refreshDashboardSpy,
+    setPeriod: setPeriodSpy,
+    loadContentReport: jasmine.createSpy('loadContentReport'),
+  };
+
+  beforeEach(async () => {
+    loadDashboardSpy.calls.reset();
+    refreshDashboardSpy.calls.reset();
+    setPeriodSpy.calls.reset();
+
+    await TestBed.configureTestingModule({
+      imports: [AnalyticsDashboardComponent, NoopAnimationsModule],
+      providers: [
+        provideRouter([]),
+        provideHttpClient(),
+        { provide: AnalyticsStore, useValue: mockStore },
+        { provide: AnalyticsService, useValue: jasmine.createSpyObj('AnalyticsService', ['getContentReport']) },
+      ],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(AnalyticsDashboardComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should call store.loadDashboard on init', () => {
+    fixture.detectChanges();
+    expect(loadDashboardSpy).toHaveBeenCalledTimes(1);
+  });
+
+  it('should show loading state when store.loading is true', () => {
+    loadingSignal.set(true);
+    summarySignal.set(null);
+    fixture.detectChanges();
+
+    const skeletons = fixture.nativeElement.querySelectorAll('p-skeleton');
+    expect(skeletons.length).toBeGreaterThan(0);
+  });
+
+  it('should render KPI cards when summary is available', () => {
+    summarySignal.set(mockSummary);
+    loadingSignal.set(false);
+    fixture.detectChanges();
+
+    const kpiCards = fixture.nativeElement.querySelector('app-dashboard-kpi-cards');
+    expect(kpiCards).toBeTruthy();
+  });
+
+  it('should show staleness indicator when data is stale', () => {
+    isStaleSignal.set(true);
+    lastRefreshedSignal.set(new Date(Date.now() - 60 * 60 * 1000).toISOString());
+    fixture.detectChanges();
+
+    const staleText = fixture.nativeElement.querySelector('.staleness-text.stale');
+    expect(staleText).toBeTruthy();
+  });
+
+  it('should trigger refreshDashboard on refresh button click', () => {
+    fixture.detectChanges();
+    component.onRefresh();
+    expect(refreshDashboardSpy).toHaveBeenCalledTimes(1);
+  });
+
+  it('should propagate period changes to store', () => {
+    fixture.detectChanges();
+    component.onPeriodChanged('7d');
+    expect(setPeriodSpy).toHaveBeenCalledWith('7d');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
index d1a1438..c9100aa 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
@@ -1,53 +1,134 @@
 import { Component, inject, OnInit } from '@angular/core';
 import { CommonModule } from '@angular/common';
 import { Router } from '@angular/router';
+import { ButtonModule } from 'primeng/button';
+import { Skeleton } from 'primeng/skeleton';
 import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
-import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
 import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
-import { DateRangePickerComponent } from './components/date-range-picker.component';
 import { EngagementChartComponent } from './components/engagement-chart.component';
 import { TopContentTableComponent } from './components/top-content-table.component';
+import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.component';
+import { DateRangeSelectorComponent } from './components/date-range-selector.component';
 import { AnalyticsStore } from './store/analytics.store';
+import { DashboardPeriod } from './models/dashboard.model';
 
 @Component({
   selector: 'app-analytics-dashboard',
   standalone: true,
   imports: [
-    CommonModule, PageHeaderComponent, LoadingSpinnerComponent, EmptyStateComponent,
-    DateRangePickerComponent, EngagementChartComponent, TopContentTableComponent,
+    CommonModule, ButtonModule, Skeleton,
+    PageHeaderComponent, EmptyStateComponent,
+    EngagementChartComponent, TopContentTableComponent,
+    DashboardKpiCardsComponent, DateRangeSelectorComponent,
   ],
   template: `
-    <app-page-header title="Analytics" />
-
-    <div class="mb-3">
-      <app-date-range-picker (rangeChanged)="onRangeChanged($event)" />
+    <div class="dashboard-header">
+      <app-page-header title="Brand Analytics" />
+      <div class="header-controls">
+        <app-date-range-selector
+          [activePeriod]="store.period()"
+          (periodChanged)="onPeriodChanged($event)"
+        />
+        <p-button
+          icon="pi pi-refresh"
+          [text]="true"
+          [loading]="store.loading()"
+          (onClick)="onRefresh()"
+          pTooltip="Refresh data"
+        />
+        @if (store.lastRefreshedAt(); as ts) {
+          <span class="staleness-text" [class.stale]="store.isStale()">
+            Updated {{ getRelativeTime(ts) }}
+          </span>
+        }
+      </div>
     </div>
 
-    @if (store.loading()) {
-      <app-loading-spinner message="Loading analytics..." />
-    } @else if (store.topContent().length === 0) {
+    @if (store.loading() && !store.summary()) {
+      <div class="kpi-skeleton-grid">
+        @for (i of skeletonCards; track i) {
+          <p-skeleton width="100%" height="100px" borderRadius="12px" />
+        }
+      </div>
+      <p-skeleton width="100%" height="300px" borderRadius="12px" styleClass="mt-3" />
+    } @else if (!store.summary() && store.topContent().length === 0) {
       <app-empty-state message="No analytics data yet. Publish content to see performance." icon="pi pi-chart-line" />
     } @else {
-      <app-engagement-chart [items]="store.topContent()" />
+      <app-dashboard-kpi-cards [summary]="store.summary()" />
+
+      <div class="charts-row mt-3">
+        <div class="chart-placeholder">
+          <app-engagement-chart [items]="store.topContent()" />
+        </div>
+        <div class="chart-placeholder"></div>
+      </div>
+
       <div class="mt-3">
         <app-top-content-table [items]="store.topContent()" (viewDetail)="viewDetail($event)" />
       </div>
     }
   `,
+  styles: `
+    .dashboard-header {
+      display: flex;
+      align-items: center;
+      justify-content: space-between;
+      flex-wrap: wrap;
+      gap: 1rem;
+      margin-bottom: 1.5rem;
+    }
+    .header-controls {
+      display: flex;
+      align-items: center;
+      gap: 0.75rem;
+      flex-wrap: wrap;
+    }
+    .staleness-text {
+      font-size: 0.8rem;
+      color: var(--p-text-muted-color, #71717a);
+    }
+    .staleness-text.stale {
+      color: #f59e0b;
+    }
+    .kpi-skeleton-grid {
+      display: grid;
+      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
+      gap: 1rem;
+    }
+    .charts-row {
+      display: grid;
+      grid-template-columns: 2fr 1fr;
+      gap: 1rem;
+    }
+  `,
 })
 export class AnalyticsDashboardComponent implements OnInit {
   private readonly router = inject(Router);
   readonly store = inject(AnalyticsStore);
+  readonly skeletonCards = [1, 2, 3, 4, 5, 6];
 
   ngOnInit() {
     this.store.loadDashboard();
   }
 
-  onRangeChanged(range: { from: string; to: string }) {
-    this.store.setPeriod(range);
+  onPeriodChanged(period: DashboardPeriod) {
+    this.store.setPeriod(period);
+  }
+
+  onRefresh() {
+    this.store.refreshDashboard();
   }
 
   viewDetail(contentId: string) {
     this.router.navigate(['/analytics', contentId]);
   }
+
+  getRelativeTime(iso: string): string {
+    const diffMs = Date.now() - new Date(iso).getTime();
+    const mins = Math.floor(diffMs / 60000);
+    if (mins < 1) return 'just now';
+    if (mins < 60) return `${mins}m ago`;
+    const hours = Math.floor(mins / 60);
+    return `${hours}h ago`;
+  }
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts
new file mode 100644
index 0000000..b2c342f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts
@@ -0,0 +1,99 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { DashboardKpiCardsComponent } from './dashboard-kpi-cards.component';
+import { DashboardSummary } from '../models/dashboard.model';
+
+describe('DashboardKpiCardsComponent', () => {
+  let component: DashboardKpiCardsComponent;
+  let fixture: ComponentFixture<DashboardKpiCardsComponent>;
+
+  const mockSummary: DashboardSummary = {
+    totalEngagement: 12847,
+    previousEngagement: 10878,
+    totalImpressions: 284000,
+    previousImpressions: 250000,
+    engagementRate: 4.52,
+    previousEngagementRate: 4.35,
+    contentPublished: 12,
+    previousContentPublished: 10,
+    costPerEngagement: 0.03,
+    previousCostPerEngagement: 0.04,
+    websiteUsers: 1200,
+    previousWebsiteUsers: 1000,
+    generatedAt: '2026-03-25T00:00:00Z',
+  };
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [DashboardKpiCardsComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(DashboardKpiCardsComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should render all 6 KPI cards with correct values', () => {
+    fixture.componentRef.setInput('summary', mockSummary);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.kpi-card');
+    expect(cards.length).toBe(6);
+
+    const labels = Array.from(cards).map((c: any) => c.querySelector('.kpi-label')?.textContent?.trim());
+    expect(labels).toContain('Total Engagement');
+    expect(labels).toContain('Total Impressions');
+    expect(labels).toContain('Engagement Rate');
+    expect(labels).toContain('Content Published');
+    expect(labels).toContain('Cost / Engagement');
+    expect(labels).toContain('Website Users');
+  });
+
+  it('should show up trend indicator for positive change', () => {
+    fixture.componentRef.setInput('summary', mockSummary);
+    fixture.detectChanges();
+
+    const kpiCards = component.kpiCards();
+    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
+    expect(engagement?.trend).toBe('up');
+    expect(engagement?.changeText).toMatch(/^\+\d+\.\d+%$/);
+  });
+
+  it('should show down trend indicator for negative change', () => {
+    const downSummary = { ...mockSummary, totalEngagement: 8000, previousEngagement: 10000 };
+    fixture.componentRef.setInput('summary', downSummary);
+    fixture.detectChanges();
+
+    const kpiCards = component.kpiCards();
+    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
+    expect(engagement?.trend).toBe('down');
+    expect(engagement?.changeText).toMatch(/^-\d+\.\d+%$/);
+  });
+
+  it('should show N/A when previous period value is 0', () => {
+    const zeroSummary = { ...mockSummary, previousEngagement: 0 };
+    fixture.componentRef.setInput('summary', zeroSummary);
+    fixture.detectChanges();
+
+    const kpiCards = component.kpiCards();
+    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
+    expect(engagement?.changeText).toBe('N/A');
+    expect(engagement?.trend).toBe('neutral');
+  });
+
+  it('should format large numbers with abbreviations', () => {
+    fixture.componentRef.setInput('summary', mockSummary);
+    fixture.detectChanges();
+
+    const kpiCards = component.kpiCards();
+    const impressions = kpiCards.find(c => c.label === 'Total Impressions');
+    expect(impressions?.value).toBe('284K');
+  });
+
+  it('should format engagement rate as percentage', () => {
+    fixture.componentRef.setInput('summary', mockSummary);
+    fixture.detectChanges();
+
+    const kpiCards = component.kpiCards();
+    const rate = kpiCards.find(c => c.label === 'Engagement Rate');
+    expect(rate?.value).toBe('4.52%');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.ts
new file mode 100644
index 0000000..21dc2ee
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.ts
@@ -0,0 +1,128 @@
+import { Component, computed, input } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { DashboardSummary } from '../models/dashboard.model';
+
+type KpiFormat = 'number' | 'abbreviated' | 'percent' | 'currency';
+
+interface KpiCard {
+  readonly label: string;
+  readonly value: string;
+  readonly changeText: string;
+  readonly trend: 'up' | 'down' | 'neutral';
+}
+
+function formatKpiValue(value: number, format: KpiFormat): string {
+  switch (format) {
+    case 'abbreviated':
+      if (value >= 1_000_000) return (value / 1_000_000).toFixed(1) + 'M';
+      if (value >= 1_000) return Math.round(value / 1_000) + 'K';
+      return value.toLocaleString('en-US');
+    case 'percent':
+      return value.toFixed(2) + '%';
+    case 'currency':
+      return '$' + value.toFixed(2);
+    default:
+      return value.toLocaleString('en-US');
+  }
+}
+
+function computeChange(current: number, previous: number): { text: string; trend: 'up' | 'down' | 'neutral' } {
+  if (previous === 0) return { text: 'N/A', trend: 'neutral' };
+  const pct = Math.round(((current - previous) / previous) * 10000) / 100;
+  if (pct > 0) return { text: '+' + pct.toFixed(1) + '%', trend: 'up' };
+  if (pct < 0) return { text: pct.toFixed(1) + '%', trend: 'down' };
+  return { text: '0%', trend: 'neutral' };
+}
+
+const KPI_DEFS: readonly { label: string; current: keyof DashboardSummary; previous: keyof DashboardSummary; format: KpiFormat }[] = [
+  { label: 'Total Engagement', current: 'totalEngagement', previous: 'previousEngagement', format: 'number' },
+  { label: 'Total Impressions', current: 'totalImpressions', previous: 'previousImpressions', format: 'abbreviated' },
+  { label: 'Engagement Rate', current: 'engagementRate', previous: 'previousEngagementRate', format: 'percent' },
+  { label: 'Content Published', current: 'contentPublished', previous: 'previousContentPublished', format: 'number' },
+  { label: 'Cost / Engagement', current: 'costPerEngagement', previous: 'previousCostPerEngagement', format: 'currency' },
+  { label: 'Website Users', current: 'websiteUsers', previous: 'previousWebsiteUsers', format: 'number' },
+];
+
+@Component({
+  selector: 'app-dashboard-kpi-cards',
+  standalone: true,
+  imports: [CommonModule],
+  template: `
+    <div class="kpi-grid">
+      @for (card of kpiCards(); track card.label) {
+        <div class="kpi-card">
+          <div class="kpi-label">{{ card.label }}</div>
+          <div class="kpi-value">{{ card.value }}</div>
+          <span class="kpi-trend" [class.up]="card.trend === 'up'" [class.down]="card.trend === 'down'">
+            @if (card.trend === 'up') { &#9650; }
+            @if (card.trend === 'down') { &#9660; }
+            {{ card.changeText }}
+          </span>
+        </div>
+      }
+    </div>
+  `,
+  styles: `
+    .kpi-grid {
+      display: grid;
+      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
+      gap: 1rem;
+    }
+    .kpi-card {
+      background: var(--p-surface-900, #111118);
+      border: 1px solid var(--p-surface-700, #25252f);
+      border-radius: 12px;
+      padding: 1.1rem 1.25rem;
+      transition: border-color 0.2s ease, transform 0.2s ease;
+    }
+    .kpi-card:hover {
+      border-color: var(--p-surface-600, #3a3a48);
+      transform: translateY(-1px);
+    }
+    .kpi-label {
+      font-size: 0.72rem;
+      font-weight: 600;
+      text-transform: uppercase;
+      letter-spacing: 0.06em;
+      color: var(--p-text-muted-color, #71717a);
+      margin-bottom: 0.5rem;
+    }
+    .kpi-value {
+      font-size: 1.65rem;
+      font-weight: 800;
+      letter-spacing: -0.03em;
+      line-height: 1.1;
+      margin-bottom: 0.4rem;
+    }
+    .kpi-trend {
+      font-size: 0.75rem;
+      font-weight: 600;
+      display: inline-flex;
+      align-items: center;
+      gap: 0.25rem;
+      padding: 0.15rem 0.5rem;
+      border-radius: 6px;
+    }
+    .kpi-trend.up { color: #22c55e; background: rgba(34, 197, 94, 0.12); }
+    .kpi-trend.down { color: #ef4444; background: rgba(239, 68, 68, 0.12); }
+  `,
+})
+export class DashboardKpiCardsComponent {
+  readonly summary = input<DashboardSummary | null>(null);
+
+  readonly kpiCards = computed<readonly KpiCard[]>(() => {
+    const s = this.summary();
+    if (!s) return [];
+    return KPI_DEFS.map(def => {
+      const current = s[def.current] as number;
+      const previous = s[def.previous] as number;
+      const change = computeChange(current, previous);
+      return {
+        label: def.label,
+        value: formatKpiValue(current, def.format),
+        changeText: change.text,
+        trend: change.trend,
+      };
+    });
+  });
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts
new file mode 100644
index 0000000..f203223
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts
@@ -0,0 +1,57 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { NoopAnimationsModule } from '@angular/platform-browser/animations';
+import { DateRangeSelectorComponent } from './date-range-selector.component';
+import { DashboardPeriod } from '../models/dashboard.model';
+
+describe('DateRangeSelectorComponent', () => {
+  let component: DateRangeSelectorComponent;
+  let fixture: ComponentFixture<DateRangeSelectorComponent>;
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [DateRangeSelectorComponent, NoopAnimationsModule],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(DateRangeSelectorComponent);
+    component = fixture.componentInstance;
+    fixture.detectChanges();
+  });
+
+  it('should emit periodChanged on preset button click', () => {
+    let emitted: DashboardPeriod | undefined;
+    component.periodChanged.subscribe((p) => (emitted = p));
+
+    component.selectPreset('7d');
+
+    expect(emitted).toBe('7d');
+  });
+
+  it('should highlight active preset with filled style', () => {
+    fixture.componentRef.setInput('activePeriod', '14d');
+    fixture.detectChanges();
+
+    expect(component.isActive('14d')).toBeTrue();
+    expect(component.isActive('30d')).toBeFalse();
+  });
+
+  it('should emit custom date range from calendar', () => {
+    let emitted: DashboardPeriod | undefined;
+    component.periodChanged.subscribe((p) => (emitted = p));
+
+    const from = new Date('2026-01-01');
+    const to = new Date('2026-01-31');
+    component.customRange = [from, to];
+    component.onCustomSelect();
+
+    expect(emitted).toBeTruthy();
+    expect(typeof emitted).toBe('object');
+    if (typeof emitted === 'object' && emitted !== null && 'from' in emitted) {
+      expect(emitted.from).toBe(from.toISOString());
+      expect(emitted.to).toBe(to.toISOString());
+    }
+  });
+
+  it('should default to 30D preset on initialization', () => {
+    expect(component.isActive('30d')).toBeTrue();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.ts
new file mode 100644
index 0000000..becaee7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.ts
@@ -0,0 +1,60 @@
+import { Component, input, output } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { FormsModule } from '@angular/forms';
+import { ButtonModule } from 'primeng/button';
+import { DatePicker } from 'primeng/datepicker';
+import { DashboardPeriod } from '../models/dashboard.model';
+
+const PRESETS = ['1d', '7d', '14d', '30d', '90d'] as const;
+
+@Component({
+  selector: 'app-date-range-selector',
+  standalone: true,
+  imports: [CommonModule, FormsModule, ButtonModule, DatePicker],
+  template: `
+    <div class="flex align-items-center gap-2 flex-wrap">
+      @for (preset of presets; track preset) {
+        <p-button
+          [label]="preset.toUpperCase()"
+          [outlined]="!isActive(preset)"
+          [severity]="isActive(preset) ? 'primary' : 'secondary'"
+          size="small"
+          (onClick)="selectPreset(preset)"
+        />
+      }
+      <p-datepicker
+        [(ngModel)]="customRange"
+        selectionMode="range"
+        [showIcon]="true"
+        placeholder="Custom range"
+        styleClass="w-14rem"
+        (onSelect)="onCustomSelect()"
+      />
+    </div>
+  `,
+})
+export class DateRangeSelectorComponent {
+  readonly activePeriod = input<DashboardPeriod>('30d');
+  readonly periodChanged = output<DashboardPeriod>();
+
+  readonly presets = PRESETS;
+  customRange: Date[] = [];
+
+  isActive(preset: string): boolean {
+    return this.activePeriod() === preset;
+  }
+
+  selectPreset(preset: string) {
+    this.customRange = [];
+    this.periodChanged.emit(preset as DashboardPeriod);
+  }
+
+  onCustomSelect() {
+    if (this.customRange.length >= 2 && this.customRange[1]) {
+      this.periodChanged.emit({
+        from: this.customRange[0].toISOString(),
+        to: this.customRange[1].toISOString(),
+      });
+    }
+  }
+}
