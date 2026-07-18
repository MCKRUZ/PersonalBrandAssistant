diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts
index d877668..e992a3c 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts
@@ -1,50 +1,55 @@
 import { TestBed } from '@angular/core/testing';
 import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
+import { NoopAnimationsModule } from '@angular/platform-browser/animations';
 import { AnalyticsComponent } from './analytics.component';
-import { WebsiteAnalytics } from './models/analytics.model';
+import { AnalyticsOverview } from './models/channel-analytics.model';
 
-describe('AnalyticsComponent', () => {
+describe('AnalyticsComponent (shell)', () => {
   let httpMock: HttpTestingController;
 
-  const stub: WebsiteAnalytics = {
-    overview: { activeUsers: 123, sessions: 200, pageViews: 500, avgSessionDuration: 90, bounceRate: 0.4, newUsers: 80 },
-    topPages: [{ pagePath: '/blog', views: 50, uniqueUsers: 30 }],
-    trafficSources: [{ channel: 'Organic Search', sessions: 100, users: 80 }],
-    searchQueries: [{ query: 'ai tools', clicks: 50, impressions: 1000, ctr: 0.05, position: 3.2 }],
-  };
+  const overviewStub: AnalyticsOverview = { totalAudience: 100, combinedKpis: [], channels: [] };
 
   beforeEach(() => {
     TestBed.configureTestingModule({
-      imports: [AnalyticsComponent, HttpClientTestingModule],
+      imports: [AnalyticsComponent, HttpClientTestingModule, NoopAnimationsModule],
     });
     httpMock = TestBed.inject(HttpTestingController);
   });
 
   afterEach(() => httpMock.verify());
 
-  it('loads website analytics on init and renders active users', () => {
+  it('renders the five source tabs', () => {
     const fixture = TestBed.createComponent(AnalyticsComponent);
     fixture.detectChanges();
 
-    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
-    const health = httpMock.match('/api/analytics/health');
-    health.forEach(r => r.flush({ ga4: true, searchConsole: true }));
-
+    // Overview mounts eagerly and fires its load; flush it so verify() stays clean.
+    httpMock.expectOne('/api/analytics/overview?period=30d').flush(overviewStub);
     fixture.detectChanges();
+
     const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
-    expect(text).toContain('123');
+    for (const label of ['Overview', 'Website', 'YouTube', 'Instagram', 'TikTok']) {
+      expect(text).toContain(label);
+    }
   });
 
-  it('shows an unavailable banner when a health source is down', () => {
+  it('lazy-loads a channel tab: no channel request before activation, exactly one after', () => {
     const fixture = TestBed.createComponent(AnalyticsComponent);
     fixture.detectChanges();
+    httpMock.expectOne('/api/analytics/overview?period=30d').flush(overviewStub);
+    fixture.detectChanges();
 
-    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
-    const health = httpMock.match('/api/analytics/health');
-    health.forEach(r => r.flush({ ga4: false, searchConsole: true }));
+    // Before activating YouTube: no channel request exists.
+    expect(httpMock.match('/api/analytics/channel/youtube?period=30d').length).toBe(0);
 
+    // Activate the YouTube tab.
+    fixture.componentInstance.onTabChange('youtube');
     fixture.detectChanges();
-    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
-    expect(text).toContain('unavailable');
+
+    // Exactly one channel request now fires.
+    const reqs = httpMock.match('/api/analytics/channel/youtube?period=30d');
+    expect(reqs.length).toBe(1);
+    reqs[0].flush({ platform: 'YouTube', status: 'NotConnected', asOf: null, kpis: [], trends: [], recentPosts: [] });
+    fixture.detectChanges();
+    // NotConnected => no deep call; nothing else outstanding.
   });
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts
index c7ff155..6cdcf7f 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts
@@ -1,417 +1,59 @@
-import { Component, OnInit, computed, signal } from '@angular/core';
+import { Component, signal } from '@angular/core';
 import { CommonModule } from '@angular/common';
-import { TableModule } from 'primeng/table';
-import { ChartModule } from 'primeng/chart';
-import { SelectButtonModule } from 'primeng/selectbutton';
-import { TooltipModule } from 'primeng/tooltip';
-import { FormsModule } from '@angular/forms';
-import { AnalyticsService } from './services/analytics.service';
-import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from './models/analytics.model';
+import { TabsModule } from 'primeng/tabs';
+import { OverviewComponent } from './overview/overview.component';
+import { WebsiteAnalyticsComponent } from './website/website-analytics.component';
+import { ChannelAnalyticsComponent } from './channel/channel-analytics.component';
 
-interface Kpi {
-  readonly icon: string;
-  readonly label: string;
-  readonly value: string;
-  readonly desc: string;
-}
-
-interface LegendRow {
-  readonly channel: string;
-  readonly sessions: number;
-  readonly pct: number;
-  readonly color: string;
-}
-
-// Channel palette drawn from the app's status/accent tokens (obsidian theme).
-const TRAFFIC_COLORS = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24', '#5a5a66', '#f0935f'];
+type TabKey = 'overview' | 'website' | 'youtube' | 'instagram' | 'tiktok';
 
 @Component({
   selector: 'app-analytics',
   standalone: true,
-  imports: [CommonModule, FormsModule, TableModule, ChartModule, SelectButtonModule, TooltipModule],
+  imports: [CommonModule, TabsModule, OverviewComponent, WebsiteAnalyticsComponent, ChannelAnalyticsComponent],
   template: `
-    <div class="analytics">
-      <header class="page-head">
-        <div class="page-head-text">
-          <h1 class="page-title">Website Analytics</h1>
-          <p class="page-sub">matthewkruczek.ai · last {{ periodLabel() }}</p>
-        </div>
-        <p-selectButton
-          class="period-select"
-          [options]="periodOptions"
-          [ngModel]="period()"
-          (ngModelChange)="changePeriod($event)"
-          optionLabel="label" optionValue="value" />
-      </header>
-
-      @if (health(); as h) {
-        @if (!h.ga4 || !h.searchConsole) {
-          <div class="banner" role="status">
-            <i class="pi pi-exclamation-triangle"></i>
-            <span>
-              Some analytics sources are unavailable
-              @if (!h.ga4) { <strong> · Google Analytics</strong> }
-              @if (!h.searchConsole) { <strong> · Search Console</strong> }
-            </span>
-          </div>
-        }
-      }
-
-      @if (loading()) {
-        <div class="kpi-grid">
-          @for (i of skeletonCells; track i) {
-            <div class="kpi-card skeleton-card"><div class="sk sk-icon"></div><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
-          }
-        </div>
-        <div class="panels">
-          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
-          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
-        </div>
-      } @else if (data()) {
-        @if (data(); as d) {
-        <section class="kpi-grid">
-          @for (k of kpis(); track k.label) {
-            <div class="kpi-card">
-              <div class="kpi-top">
-                <span class="kpi-icon"><i class="pi {{ k.icon }}"></i></span>
-                <span class="kpi-label">{{ k.label }}</span>
-                <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
-                   [attr.aria-label]="k.label + ': ' + k.desc"
-                   [pTooltip]="k.desc" tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
-              </div>
-              <div class="kpi-value">{{ k.value }}</div>
-            </div>
-          }
-        </section>
-
-        <section class="panels">
-          <div class="panel">
-            <div class="panel-head">
-              <h2>Traffic Sources</h2>
-              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
-                 aria-label="Traffic Sources: how visitors arrived"
-                 pTooltip="How visitors arrived, grouped by channel — direct, referral, organic search, social (Google Analytics)."
-                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
-            </div>
-            @if (d.trafficSources.length) {
-              <div class="doughnut-wrap">
-                <p-chart type="doughnut" [data]="trafficData()" [options]="chartOptions" />
-                <div class="doughnut-center">
-                  <span class="dc-value">{{ totalSessions() | number }}</span>
-                  <span class="dc-label">sessions</span>
-                </div>
-              </div>
-              <ul class="legend">
-                @for (row of trafficLegend(); track row.channel) {
-                  <li>
-                    <span class="dot" [style.background]="row.color"></span>
-                    <span class="legend-name">{{ row.channel }}</span>
-                    <span class="legend-val">{{ row.sessions | number }}</span>
-                    <span class="legend-pct">{{ row.pct | number:'1.0-0' }}%</span>
-                  </li>
-                }
-              </ul>
-            } @else {
-              <div class="empty"><i class="pi pi-chart-pie"></i><p>No traffic data</p></div>
-            }
-          </div>
-
-          <div class="panel">
-            <div class="panel-head">
-              <h2>Top Pages</h2>
-              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
-                 aria-label="Top Pages: most-viewed pages"
-                 pTooltip="Your most-viewed pages this period, with total views and the unique visitors who saw each (Google Analytics)."
-                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
-            </div>
-            @if (d.topPages.length) {
-              <table class="data-table">
-                <thead><tr><th>Page</th><th class="num">Views</th><th class="num">Users</th></tr></thead>
-                <tbody>
-                  @for (row of d.topPages; track row.pagePath) {
-                    <tr>
-                      <td class="path" [title]="row.pagePath">
-                        <span class="path-text">{{ row.pagePath }}</span>
-                        <span class="bar"><span class="bar-fill" [style.width.%]="barWidth(row.views)"></span></span>
-                      </td>
-                      <td class="num strong">{{ row.views | number }}</td>
-                      <td class="num">{{ row.uniqueUsers | number }}</td>
-                    </tr>
-                  }
-                </tbody>
-              </table>
-            } @else {
-              <div class="empty"><i class="pi pi-file"></i><p>No page data</p></div>
-            }
-          </div>
-        </section>
-
-        <section class="panel">
-          <div class="panel-head">
-            <h2>Top Search Queries</h2>
-            <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
-               aria-label="Top Search Queries: Google searches where the site appeared"
-               pTooltip="Google searches where your site appeared. Impressions = times shown, Clicks = visits from search, CTR = click rate, Position = average rank (lower is better). Source: Search Console."
-               tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
-          </div>
-          @if (d.searchQueries.length) {
-            <table class="data-table queries">
-              <thead>
-                <tr><th>Query</th><th class="num">Clicks</th><th class="num">Impr.</th><th class="num">CTR</th><th class="num">Position</th></tr>
-              </thead>
-              <tbody>
-                @for (row of d.searchQueries; track row.query) {
-                  <tr>
-                    <td class="query" [title]="row.query">{{ row.query }}</td>
-                    <td class="num strong">{{ row.clicks | number }}</td>
-                    <td class="num">{{ row.impressions | number }}</td>
-                    <td class="num">{{ (row.ctr * 100) | number:'1.0-1' }}%</td>
-                    <td class="num"><span class="pos" [class]="posClass(row.position)">{{ row.position | number:'1.0-1' }}</span></td>
-                  </tr>
-                }
-              </tbody>
-            </table>
-          } @else {
-            <div class="empty"><i class="pi pi-search"></i><p>No search queries</p></div>
-          }
-        </section>
-        }
-      } @else {
-        <div class="empty empty-page">
-          <i class="pi pi-chart-bar"></i>
-          <p>No analytics data available</p>
-        </div>
-      }
-    </div>
+    <p-tabs [value]="activeTab()" (valueChange)="onTabChange($event)" class="analytics-tabs">
+      <p-tablist>
+        <p-tab value="overview">Overview</p-tab>
+        <p-tab value="website">Website</p-tab>
+        <p-tab value="youtube">YouTube</p-tab>
+        <p-tab value="instagram">Instagram</p-tab>
+        <p-tab value="tiktok">TikTok</p-tab>
+      </p-tablist>
+      <p-tabpanels>
+        <p-tabpanel value="overview">
+          @if (visited().has('overview')) { <app-overview /> }
+        </p-tabpanel>
+        <p-tabpanel value="website">
+          @if (visited().has('website')) { <app-website-analytics /> }
+        </p-tabpanel>
+        <p-tabpanel value="youtube">
+          @if (visited().has('youtube')) { <app-channel-analytics [platform]="'youtube'" /> }
+        </p-tabpanel>
+        <p-tabpanel value="instagram">
+          @if (visited().has('instagram')) { <app-channel-analytics [platform]="'instagram'" /> }
+        </p-tabpanel>
+        <p-tabpanel value="tiktok">
+          @if (visited().has('tiktok')) { <app-channel-analytics [platform]="'tiktok'" /> }
+        </p-tabpanel>
+      </p-tabpanels>
+    </p-tabs>
   `,
   styles: [`
     :host { display: block; }
-    .analytics { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }
-
-    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
-    .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
-    .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }
-
-    .banner {
-      display: flex; align-items: center; gap: 10px;
-      background: var(--delivery-warn-bg); color: var(--delivery-warn-fg);
-      border: 1px solid color-mix(in srgb, var(--delivery-warn-fg) 30%, transparent);
-      border-radius: var(--r-inner); padding: 11px 14px; margin-bottom: 20px; font-size: 13px;
-    }
-    .banner strong { font-weight: 600; }
-
-    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
-    .kpi-card {
-      background: var(--surface-card); border: 1px solid var(--surface-border);
-      border-radius: var(--r); padding: 16px 18px;
-      display: flex; flex-direction: column; gap: 12px;
-      transition: border-color .14s, box-shadow .14s;
-    }
-    .kpi-card:hover { border-color: var(--surface-disabled); box-shadow: 0 6px 20px -12px rgba(0,0,0,.6); }
-    .kpi-top { display: flex; align-items: center; gap: 10px; }
-    .kpi-top .kpi-label { flex: 1; }
-    .info-icon {
-      font-size: 13px; color: var(--text-secondary); cursor: help;
-      transition: color .14s; border-radius: 99px; outline: none;
-    }
-    .info-icon:hover, .info-icon:focus-visible { color: var(--brand-primary); }
-    .info-icon:focus-visible { box-shadow: 0 0 0 2px color-mix(in srgb, var(--brand-primary) 50%, transparent); }
-    .kpi-icon {
-      width: 34px; height: 34px; border-radius: var(--r-control);
-      background: var(--accent-soft); color: var(--brand-primary);
-      display: grid; place-items: center; font-size: 15px; flex-shrink: 0;
-    }
-    .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
-    .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
-
-    .panels { display: grid; grid-template-columns: minmax(300px, 5fr) minmax(0, 7fr); gap: 14px; margin-bottom: 14px; }
-    @media (max-width: 880px) { .panels { grid-template-columns: 1fr; } }
-
-    .panel { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 18px 20px; }
-    .panel-head { display: flex; align-items: center; gap: 7px; margin-bottom: 14px; }
-    .panel-head h2 { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); margin: 0; }
-
-    .doughnut-wrap { position: relative; height: 200px; margin-bottom: 12px; }
-    .doughnut-center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
-    .dc-value { font-family: var(--font-mono); font-size: 22px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
-    .dc-label { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--text-secondary); }
-
-    .legend { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
-    .legend li { display: flex; align-items: center; gap: 10px; font-size: 13px; }
-    .dot { width: 9px; height: 9px; border-radius: 99px; flex-shrink: 0; }
-    .legend-name { color: var(--text-primary); flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
-    .legend-val { font-family: var(--font-mono); color: var(--text-primary); font-variant-numeric: tabular-nums; }
-    .legend-pct { font-family: var(--font-mono); color: var(--text-secondary); min-width: 36px; text-align: right; font-variant-numeric: tabular-nums; }
-
-    .data-table { width: 100%; border-collapse: collapse; font-size: 13px; }
-    .data-table th {
-      text-align: left; font-weight: 500; color: var(--text-secondary);
-      font-size: 11px; letter-spacing: .04em; text-transform: uppercase;
-      padding: 8px 12px; border-bottom: 1px solid var(--surface-border);
-    }
-    .data-table td { padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 55%, transparent); color: var(--text-primary); }
-    .data-table tbody tr:last-child td { border-bottom: 0; }
-    .data-table tbody tr { transition: background .12s; }
-    .data-table tbody tr:hover { background: var(--surface-hover); }
-    .num { text-align: right; font-family: var(--font-mono); font-variant-numeric: tabular-nums; white-space: nowrap; }
-    th.num { text-align: right; }
-    .strong { color: var(--text-primary); font-weight: 600; }
-    .data-table td.num:not(.strong) { color: var(--text-secondary); }
-
-    .path { max-width: 0; }
-    .path-text { display: block; font-family: var(--font-mono); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
-    .bar { display: block; height: 3px; margin-top: 6px; background: var(--surface-hover); border-radius: 99px; overflow: hidden; }
-    .bar-fill { display: block; height: 100%; background: var(--brand-primary); border-radius: 99px; }
-    .query { max-width: 320px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
-
-    .pos { font-family: var(--font-mono); padding: 2px 8px; border-radius: var(--r-pill); font-size: 12px; }
-    .pos.good { background: color-mix(in srgb, var(--status-approved) 16%, transparent); color: var(--status-approved); }
-    .pos.mid { background: color-mix(in srgb, var(--score-warning, #fbbf24) 16%, transparent); color: #fbbf24; }
-    .pos.low { background: var(--surface-hover); color: var(--text-secondary); }
-
-    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 36px 16px; color: var(--text-muted); }
-    .empty i { font-size: 26px; }
-    .empty p { margin: 0; font-size: 13px; }
-    .empty-page { padding: 80px 16px; }
-
-    .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
-    .sk-icon { width: 34px; height: 34px; }
-    .sk-line { height: 11px; width: 60%; }
-    .sk-value { height: 22px; width: 80%; }
-    .skeleton-panel .sk-block { height: 200px; width: 100%; border-radius: var(--r-inner); }
-    @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
+    .analytics-tabs { display: block; }
   `],
 })
-export class AnalyticsComponent implements OnInit {
-  readonly periodOptions = [
-    { label: '7d', value: '7d' as AnalyticsPeriod },
-    { label: '30d', value: '30d' as AnalyticsPeriod },
-    { label: '90d', value: '90d' as AnalyticsPeriod },
-  ];
-  readonly skeletonCells = [0, 1, 2, 3, 4, 5];
-
-  readonly period = signal<AnalyticsPeriod>('30d');
-  readonly loading = signal(false);
-  readonly data = signal<WebsiteAnalytics | null>(null);
-  readonly health = signal<AnalyticsHealth | null>(null);
-
-  readonly chartOptions = {
-    cutout: '70%',
-    responsive: true,
-    maintainAspectRatio: false,
-    plugins: {
-      legend: { display: false },
-      tooltip: {
-        backgroundColor: '#1a1a20',
-        borderColor: '#2c2c36',
-        borderWidth: 1,
-        titleColor: '#f0f0f5',
-        bodyColor: '#8a8a96',
-        padding: 10,
-        cornerRadius: 8,
-      },
-    },
-  };
-
-  readonly periodLabel = computed(() =>
-    ({ '7d': '7 days', '30d': '30 days', '90d': '90 days' })[this.period()]);
-
-  readonly totalSessions = computed(() =>
-    (this.data()?.trafficSources ?? []).reduce((sum, s) => sum + s.sessions, 0));
-
-  readonly trafficLegend = computed<LegendRow[]>(() => {
-    const sources = this.data()?.trafficSources ?? [];
-    const total = this.totalSessions() || 1;
-    return sources.map((s, i) => ({
-      channel: s.channel,
-      sessions: s.sessions,
-      pct: (s.sessions / total) * 100,
-      color: TRAFFIC_COLORS[i % TRAFFIC_COLORS.length],
-    }));
-  });
-
-  readonly trafficData = computed(() => {
-    const sources = this.data()?.trafficSources ?? [];
-    return {
-      labels: sources.map(s => s.channel),
-      datasets: [{
-        data: sources.map(s => s.sessions),
-        backgroundColor: sources.map((_, i) => TRAFFIC_COLORS[i % TRAFFIC_COLORS.length]),
-        borderColor: '#141418',
-        borderWidth: 2,
-        hoverOffset: 4,
-      }],
-    };
-  });
-
-  readonly kpis = computed<Kpi[]>(() => {
-    const o = this.data()?.overview;
-    if (!o) return [];
-    return [
-      { icon: 'pi-users', label: 'Users', value: this.num(o.activeUsers),
-        desc: 'Distinct people who visited the site in this period. One person is counted once no matter how many times they return (GA4 active users).' },
-      { icon: 'pi-chart-line', label: 'Sessions', value: this.num(o.sessions),
-        desc: 'Individual visits to the site. A single person can start several sessions, so this is usually higher than Users (GA4).' },
-      { icon: 'pi-eye', label: 'Page Views', value: this.num(o.pageViews),
-        desc: 'Total pages loaded, including repeat views of the same page. Measures overall content consumption (GA4).' },
-      { icon: 'pi-user-plus', label: 'New Users', value: this.num(o.newUsers),
-        desc: 'First-time visitors who had never been to the site before this period (GA4).' },
-      { icon: 'pi-percentage', label: 'Bounce Rate', value: `${(o.bounceRate * 100).toFixed(1)}%`,
-        desc: 'Share of sessions where the visitor left without any meaningful interaction. Lower is better (GA4).' },
-      { icon: 'pi-clock', label: 'Avg Session', value: this.duration(o.avgSessionDuration),
-        desc: 'Average time a visitor spent on the site per session. Longer sessions suggest more engaging content (GA4).' },
-    ];
-  });
-
-  private readonly maxPageViews = computed(() =>
-    Math.max(1, ...(this.data()?.topPages ?? []).map(p => p.views)));
-
-  constructor(private readonly api: AnalyticsService) {}
-
-  ngOnInit(): void {
-    this.load();
-    this.api.getHealth().subscribe({
-      next: h => this.health.set(h),
-      error: () => this.health.set({ ga4: false, searchConsole: false }),
-    });
-  }
-
-  changePeriod(p: AnalyticsPeriod): void {
-    this.period.set(p);
-    this.load();
-  }
-
-  barWidth(views: number): number {
-    return (views / this.maxPageViews()) * 100;
-  }
-
-  posClass(position: number): string {
-    if (position <= 10) return 'good';
-    if (position <= 30) return 'mid';
-    return 'low';
-  }
-
-  private num(value: number): string {
-    return value.toLocaleString('en-US');
-  }
-
-  private duration(seconds: number): string {
-    const m = Math.floor(seconds / 60);
-    const s = Math.round(seconds % 60);
-    return m > 0 ? `${m}m ${s}s` : `${s}s`;
-  }
-
-  private load(): void {
-    this.loading.set(true);
-    this.api.getWebsite(this.period()).subscribe({
-      next: d => {
-        this.data.set(d);
-        this.loading.set(false);
-      },
-      error: () => {
-        this.data.set(null);
-        this.loading.set(false);
-      },
-    });
+export class AnalyticsComponent {
+  readonly activeTab = signal<TabKey>('overview');
+  // A tab's child mounts (and fires its HTTP) only after its tab is first opened.
+  readonly visited = signal<ReadonlySet<TabKey>>(new Set<TabKey>(['overview']));
+
+  onTabChange(value: string | number | undefined): void {
+    const tab = value as TabKey;
+    this.activeTab.set(tab);
+    if (!this.visited().has(tab)) {
+      this.visited.update(prev => new Set<TabKey>([...prev, tab]));
+    }
   }
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.spec.ts
new file mode 100644
index 0000000..f2decbf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.spec.ts
@@ -0,0 +1,122 @@
+import { TestBed } from '@angular/core/testing';
+import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
+import { ChannelAnalyticsComponent } from './channel-analytics.component';
+import { ChannelAnalytics, YouTubeDeepAnalytics } from '../models/channel-analytics.model';
+
+describe('ChannelAnalyticsComponent', () => {
+  let httpMock: HttpTestingController;
+
+  const connected: ChannelAnalytics = {
+    platform: 'YouTube',
+    status: 'Connected',
+    asOf: '2026-07-03',
+    kpis: [
+      { key: 'subscribers', label: 'Subscribers', value: 12000, deltaPct: 1.5, rate: null },
+      { key: 'engagement', label: 'Engagement', value: 340, deltaPct: null, rate: 0.042 },
+    ],
+    trends: [
+      { metric: 'Views', points: [{ date: '2026-07-01', value: 100 }, { date: '2026-07-02', value: 150 }] },
+    ],
+    recentPosts: [
+      { videoId: 'vid1', title: 'How AI ships', metrics: { views: 900, likes: 42 } },
+    ],
+  };
+
+  const deep: YouTubeDeepAnalytics = {
+    daySeries: [{ metric: 'estimatedMinutesWatched', points: [{ day: '2026-07-01', value: 500 }] }],
+    trafficSources: [{ metric: 'SUGGESTED', points: [{ day: '2026-07-01', value: 200 }] }],
+    geography: [],
+    demographics: [{ metric: 'age25-34', points: [{ day: '2026-07-01', value: 60 }] }],
+  };
+
+  function createFor(platform: 'youtube' | 'instagram' | 'tiktok') {
+    const fixture = TestBed.createComponent(ChannelAnalyticsComponent);
+    fixture.componentRef.setInput('platform', platform);
+    fixture.detectChanges();
+    return fixture;
+  }
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [ChannelAnalyticsComponent, HttpClientTestingModule],
+    });
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => httpMock.verify());
+
+  it('renders KPI row, trend charts and recent-posts table when Connected', () => {
+    const fixture = createFor('instagram');
+
+    httpMock.expectOne('/api/analytics/channel/instagram?period=30d').flush({ ...connected, platform: 'Instagram' });
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    const text = el.textContent ?? '';
+    expect(text).toContain('Subscribers');
+    expect(text).toContain('12,000');
+    // one trend chart present
+    expect(el.querySelectorAll('p-chart').length).toBeGreaterThanOrEqual(1);
+    // recent-posts table row rendered
+    expect(text).toContain('How AI ships');
+  });
+
+  it('shows a Connect affordance linking to the OAuth authorize URL when NotConnected', () => {
+    const fixture = createFor('instagram');
+
+    httpMock.expectOne('/api/analytics/channel/instagram?period=30d')
+      .flush({ platform: 'Instagram', status: 'NotConnected', asOf: null, kpis: [], trends: [], recentPosts: [] });
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    const link = el.querySelector('a.connect-btn') as HTMLAnchorElement | null;
+    expect(link).not.toBeNull();
+    expect(link!.textContent).toContain('Connect');
+    expect(link!.getAttribute('href')).toBe('/api/auth/instagram/authorize?purpose=analytics');
+  });
+
+  it('shows a Reconnect affordance when ReconnectRequired', () => {
+    const fixture = createFor('tiktok');
+
+    httpMock.expectOne('/api/analytics/channel/tiktok?period=30d')
+      .flush({ platform: 'TikTok', status: 'ReconnectRequired', asOf: null, kpis: [], trends: [], recentPosts: [] });
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    const link = el.querySelector('a.connect-btn') as HTMLAnchorElement | null;
+    expect(link).not.toBeNull();
+    expect(link!.textContent).toContain('Reconnect');
+    expect(link!.getAttribute('href')).toBe('/api/auth/tiktok/authorize?purpose=analytics');
+  });
+
+  it('renders the YouTube deep panel on success (channel + deep requests)', () => {
+    const fixture = createFor('youtube');
+
+    httpMock.expectOne('/api/analytics/channel/youtube?period=30d').flush(connected);
+    fixture.detectChanges();
+
+    httpMock.expectOne('/api/analytics/youtube/deep?period=30d').flush(deep);
+    fixture.detectChanges();
+
+    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
+    expect(text).toContain('YouTube Deep Analytics');
+    expect(text).toContain('estimatedMinutesWatched');
+  });
+
+  it('degrades gracefully when the deep call errors — rest of the tab still renders', () => {
+    const fixture = createFor('youtube');
+
+    httpMock.expectOne('/api/analytics/channel/youtube?period=30d').flush(connected);
+    fixture.detectChanges();
+
+    httpMock.expectOne('/api/analytics/youtube/deep?period=30d').error(new ProgressEvent('error'));
+    fixture.detectChanges();
+
+    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
+    // KPI/trend content still shows…
+    expect(text).toContain('Subscribers');
+    expect(text).toContain('How AI ships');
+    // …but the deep panel is absent.
+    expect(text).not.toContain('YouTube Deep Analytics');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.ts
new file mode 100644
index 0000000..14e6359
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/channel/channel-analytics.component.ts
@@ -0,0 +1,341 @@
+import { Component, OnInit, computed, input, signal } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { ChartModule } from 'primeng/chart';
+import { SelectButtonModule } from 'primeng/selectbutton';
+import { FormsModule } from '@angular/forms';
+import { AnalyticsService } from '../services/analytics.service';
+import { AnalyticsPeriod } from '../models/analytics.model';
+import {
+  ChannelAnalytics,
+  ChannelPlatform,
+  TrendSeries,
+  YouTubeDeepAnalytics,
+} from '../models/channel-analytics.model';
+
+const TREND_COLORS = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24', '#f0935f'];
+
+@Component({
+  selector: 'app-channel-analytics',
+  standalone: true,
+  imports: [CommonModule, FormsModule, ChartModule, SelectButtonModule],
+  template: `
+    <div class="channel">
+      <header class="page-head">
+        <div class="page-head-text">
+          <h1 class="page-title">{{ titleCase(platform()) }}</h1>
+          <p class="page-sub">Channel analytics · last {{ periodLabel() }}</p>
+        </div>
+        <p-selectButton
+          class="period-select"
+          [options]="periodOptions"
+          [ngModel]="period()"
+          (ngModelChange)="changePeriod($event)"
+          optionLabel="label" optionValue="value" />
+      </header>
+
+      @if (loading()) {
+        <div class="kpi-grid">
+          @for (i of skeletonCells; track i) {
+            <div class="kpi-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
+          }
+        </div>
+      } @else if (status() === 'NotConnected') {
+        <div class="connect-cta">
+          <i class="pi pi-link"></i>
+          <p>{{ titleCase(platform()) }} is not connected.</p>
+          <a class="connect-btn" [href]="authUrl()">Connect {{ titleCase(platform()) }}</a>
+        </div>
+      } @else if (status() === 'ReconnectRequired') {
+        <div class="connect-cta">
+          <i class="pi pi-exclamation-triangle"></i>
+          <p>Your {{ titleCase(platform()) }} connection expired.</p>
+          <a class="connect-btn" [href]="authUrl()">Reconnect</a>
+        </div>
+      } @else {
+        @if (data(); as d) {
+        @if (d.kpis.length) {
+          <section class="kpi-grid">
+            @for (k of d.kpis; track k.key) {
+              <div class="kpi-card">
+                <span class="kpi-label">{{ k.label }}</span>
+                <span class="kpi-value">{{ k.value | number }}</span>
+                <div class="kpi-meta">
+                  @if (k.deltaPct !== null) {
+                    <span class="kpi-delta" [class.up]="k.deltaPct >= 0" [class.down]="k.deltaPct < 0">
+                      {{ k.deltaPct >= 0 ? '▲' : '▼' }} {{ absPct(k.deltaPct) | number:'1.0-1' }}%
+                    </span>
+                  }
+                  @if (k.rate !== null) {
+                    <span class="kpi-rate">{{ (k.rate * 100) | number:'1.0-2' }}% eng.</span>
+                  }
+                </div>
+              </div>
+            }
+          </section>
+        }
+
+        @if (d.trends.length) {
+          <section class="panels">
+            @for (t of d.trends; track t.metric; let idx = $index) {
+              <div class="panel">
+                <div class="panel-head"><h2>{{ t.metric }}</h2></div>
+                <div class="trend-wrap">
+                  <p-chart type="line" [data]="trendData(t, idx)" [options]="trendOptions" />
+                </div>
+              </div>
+            }
+          </section>
+        }
+
+        @if (d.recentPosts.length) {
+          <section class="panel">
+            <div class="panel-head"><h2>Recent Posts</h2></div>
+            <table class="data-table">
+              <thead>
+                <tr>
+                  <th>Title</th>
+                  @for (col of postColumns(); track col) { <th class="num">{{ col }}</th> }
+                </tr>
+              </thead>
+              <tbody>
+                @for (row of d.recentPosts; track row.videoId) {
+                  <tr>
+                    <td class="title" [title]="row.title ?? row.videoId">{{ row.title ?? row.videoId }}</td>
+                    @for (col of postColumns(); track col) {
+                      <td class="num">{{ (row.metrics[col] ?? 0) | number }}</td>
+                    }
+                  </tr>
+                }
+              </tbody>
+            </table>
+          </section>
+        }
+
+        @if (platform() === 'youtube' && deep() && !deepFailed()) {
+          @if (deep(); as dd) {
+            <section class="deep">
+              <div class="panel-head"><h2>YouTube Deep Analytics</h2></div>
+              <div class="panels">
+                @for (s of dd.daySeries; track s.metric; let idx = $index) {
+                  <div class="panel">
+                    <div class="panel-head"><h2>{{ s.metric }}</h2></div>
+                    <div class="trend-wrap">
+                      <p-chart type="line" [data]="deepSeriesData(s, idx)" [options]="trendOptions" />
+                    </div>
+                  </div>
+                }
+              </div>
+              @if (dd.trafficSources.length) {
+                <div class="panel">
+                  <div class="panel-head"><h2>Traffic Sources</h2></div>
+                  <ul class="breakdown">
+                    @for (b of latestBreakdown(dd.trafficSources); track b.label) {
+                      <li><span class="bk-label">{{ b.label }}</span><span class="bk-value">{{ b.value | number }}</span></li>
+                    }
+                  </ul>
+                </div>
+              }
+              @if (dd.demographics.length) {
+                <div class="panel">
+                  <div class="panel-head"><h2>Demographics</h2></div>
+                  <ul class="breakdown">
+                    @for (b of latestBreakdown(dd.demographics); track b.label) {
+                      <li><span class="bk-label">{{ b.label }}</span><span class="bk-value">{{ b.value | number }}</span></li>
+                    }
+                  </ul>
+                </div>
+              }
+            </section>
+          }
+        } @else if (platform() === 'youtube' && deepFailed()) {
+          <p class="deep-unavailable">Deep analytics unavailable right now.</p>
+        }
+        } @else {
+          <div class="empty empty-page">
+            <i class="pi pi-chart-bar"></i>
+            <p>No analytics data available</p>
+          </div>
+        }
+      }
+    </div>
+  `,
+  styles: [`
+    :host { display: block; }
+    .channel { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }
+
+    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
+    .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
+    .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }
+
+    .connect-cta {
+      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px;
+      background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r);
+      padding: 56px 24px; text-align: center; color: var(--text-secondary);
+    }
+    .connect-cta i { font-size: 30px; color: var(--brand-primary); }
+    .connect-cta p { margin: 0; font-size: 14px; }
+    .connect-btn {
+      display: inline-block; margin-top: 4px; padding: 10px 20px; border-radius: var(--r-control);
+      background: var(--brand-primary); color: #fff; font-size: 14px; text-decoration: none; font-weight: 500;
+    }
+    .connect-btn:hover { filter: brightness(1.08); }
+
+    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
+    .kpi-card { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 16px 18px; display: flex; flex-direction: column; gap: 8px; }
+    .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
+    .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+    .kpi-meta { display: flex; gap: 10px; align-items: center; }
+    .kpi-delta { font-family: var(--font-mono); font-size: 12px; }
+    .kpi-delta.up { color: var(--status-approved, #4ade80); }
+    .kpi-delta.down { color: var(--status-rejected, #f0935f); }
+    .kpi-rate { font-size: 11px; color: var(--text-secondary); }
+
+    .panels { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 14px; margin-bottom: 14px; }
+    .panel { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 18px 20px; }
+    .panel-head { display: flex; align-items: center; gap: 7px; margin-bottom: 14px; }
+    .panel-head h2 { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); margin: 0; }
+    .trend-wrap { height: 200px; }
+
+    .data-table { width: 100%; border-collapse: collapse; font-size: 13px; }
+    .data-table th { text-align: left; font-weight: 500; color: var(--text-secondary); font-size: 11px; letter-spacing: .04em; text-transform: uppercase; padding: 8px 12px; border-bottom: 1px solid var(--surface-border); }
+    .data-table td { padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 55%, transparent); color: var(--text-primary); }
+    .data-table tbody tr:last-child td { border-bottom: 0; }
+    .num { text-align: right; font-family: var(--font-mono); font-variant-numeric: tabular-nums; white-space: nowrap; }
+    th.num { text-align: right; }
+    .title { max-width: 340px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
+
+    .deep { margin-top: 8px; }
+    .breakdown { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
+    .breakdown li { display: flex; justify-content: space-between; gap: 10px; font-size: 13px; }
+    .bk-label { color: var(--text-primary); }
+    .bk-value { font-family: var(--font-mono); color: var(--text-secondary); font-variant-numeric: tabular-nums; }
+    .deep-unavailable { color: var(--text-muted); font-size: 13px; font-style: italic; padding: 8px 2px; }
+
+    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 80px 16px; color: var(--text-muted); }
+    .empty i { font-size: 26px; }
+    .empty p { margin: 0; font-size: 13px; }
+
+    .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
+    .sk-line { height: 11px; width: 60%; margin-bottom: 8px; }
+    .sk-value { height: 26px; width: 80%; }
+    .skeleton-card { padding: 16px 18px; }
+    @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
+  `],
+})
+export class ChannelAnalyticsComponent implements OnInit {
+  readonly platform = input.required<ChannelPlatform>();
+
+  readonly periodOptions = [
+    { label: '7d', value: '7d' as AnalyticsPeriod },
+    { label: '30d', value: '30d' as AnalyticsPeriod },
+    { label: '90d', value: '90d' as AnalyticsPeriod },
+  ];
+  readonly skeletonCells = [0, 1, 2, 3];
+
+  readonly period = signal<AnalyticsPeriod>('30d');
+  readonly loading = signal(false);
+  readonly data = signal<ChannelAnalytics | null>(null);
+  readonly deep = signal<YouTubeDeepAnalytics | null>(null);
+  readonly deepFailed = signal(false);
+
+  readonly status = computed(() => this.data()?.status ?? null);
+
+  readonly trendOptions = {
+    responsive: true,
+    maintainAspectRatio: false,
+    plugins: { legend: { display: false } },
+    elements: { point: { radius: 0 } },
+    scales: {
+      x: { ticks: { color: '#8a8a96' }, grid: { display: false } },
+      y: { ticks: { color: '#8a8a96' }, grid: { color: 'rgba(255,255,255,0.05)' } },
+    },
+  };
+
+  readonly periodLabel = computed(() =>
+    ({ '7d': '7 days', '30d': '30 days', '90d': '90 days' })[this.period()]);
+
+  // Union of metric keys across recent posts, so the table shows every metric present.
+  readonly postColumns = computed<string[]>(() => {
+    const posts = this.data()?.recentPosts ?? [];
+    const keys = new Set<string>();
+    for (const p of posts) {
+      for (const k of Object.keys(p.metrics)) keys.add(k);
+    }
+    return [...keys];
+  });
+
+  constructor(private readonly api: AnalyticsService) {}
+
+  ngOnInit(): void {
+    this.load();
+  }
+
+  changePeriod(p: AnalyticsPeriod): void {
+    this.period.set(p);
+    this.load();
+  }
+
+  absPct(delta: number): number {
+    return Math.abs(delta);
+  }
+
+  authUrl(): string {
+    return `/api/auth/${this.platform()}/authorize?purpose=analytics`;
+  }
+
+  titleCase(platform: ChannelPlatform): string {
+    return { youtube: 'YouTube', instagram: 'Instagram', tiktok: 'TikTok' }[platform];
+  }
+
+  trendData(series: TrendSeries, index: number) {
+    const color = TREND_COLORS[index % TREND_COLORS.length];
+    return {
+      labels: series.points.map(p => p.date),
+      datasets: [{ data: series.points.map(p => p.value), borderColor: color, backgroundColor: color, borderWidth: 2, tension: 0.3, fill: false }],
+    };
+  }
+
+  deepSeriesData(series: YouTubeDeepAnalytics['daySeries'][number], index: number) {
+    const color = TREND_COLORS[index % TREND_COLORS.length];
+    return {
+      labels: series.points.map(p => p.day),
+      datasets: [{ data: series.points.map(p => p.value), borderColor: color, backgroundColor: color, borderWidth: 2, tension: 0.3, fill: false }],
+    };
+  }
+
+  // Collapse a labeled series into its latest point per label for a breakdown list.
+  latestBreakdown(series: YouTubeDeepAnalytics['trafficSources']): { label: string; value: number }[] {
+    return series.map(s => ({ label: s.metric, value: s.points.length ? s.points[s.points.length - 1].value : 0 }));
+  }
+
+  private load(): void {
+    this.loading.set(true);
+    this.deep.set(null);
+    this.deepFailed.set(false);
+    const platform = this.platform();
+    this.api.getChannel(platform, this.period()).subscribe({
+      next: d => {
+        this.data.set(d);
+        this.loading.set(false);
+        if (platform === 'youtube' && d.status === 'Connected') {
+          this.loadDeep();
+        }
+      },
+      error: () => {
+        this.data.set(null);
+        this.loading.set(false);
+      },
+    });
+  }
+
+  private loadDeep(): void {
+    this.deepFailed.set(false);
+    this.api.getYouTubeDeep(this.period()).subscribe({
+      next: dd => this.deep.set(dd),
+      error: () => {
+        this.deep.set(null);
+        this.deepFailed.set(true);
+      },
+    });
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/channel-analytics.model.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/channel-analytics.model.ts
new file mode 100644
index 0000000..76e4b05
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/models/channel-analytics.model.ts
@@ -0,0 +1,74 @@
+// TS mirrors of the section-06 read-API DTOs (PBA.Application/Features/ChannelAnalytics/Dtos).
+// JSON is camelCase; enums serialize as their string NAME (global JsonStringEnumConverter),
+// so `platform` is "YouTube"/"Instagram"/"TikTok" and `status` is one of ConnectionStatus below.
+
+export type ConnectionStatus = 'Connected' | 'ReconnectRequired' | 'NotConnected';
+
+// Lowercase route token used in /api/analytics/channel/{platform} and /api/auth/{platform}/authorize.
+export type ChannelPlatform = 'youtube' | 'instagram' | 'tiktok';
+
+// Snapshot-derived point: C# MetricPoint(DateOnly Date, long Value) -> { date: "2026-07-01", value }.
+export interface MetricPoint {
+  date: string;
+  value: number;
+}
+
+export interface TrendSeries {
+  metric: string;
+  points: MetricPoint[];
+}
+
+export interface KpiCard {
+  key: string;
+  label: string;
+  value: number;
+  deltaPct: number | null;
+  rate: number | null; // engagement-rate fraction, null except on the engagement card
+}
+
+export interface RecentPost {
+  videoId: string;
+  title: string | null;
+  metrics: Record<string, number>;
+}
+
+export interface ChannelAnalytics {
+  platform: string; // enum string name, e.g. "YouTube"
+  status: ConnectionStatus;
+  asOf: string | null;
+  kpis: KpiCard[];
+  trends: TrendSeries[];
+  recentPosts: RecentPost[];
+}
+
+export interface OverviewChannel {
+  platform: string;
+  status: ConnectionStatus;
+  followers: number | null;
+  followerSparkline: MetricPoint[];
+}
+
+export interface AnalyticsOverview {
+  totalAudience: number; // sum across connected channels — labeled APPROXIMATE in the UI
+  combinedKpis: KpiCard[];
+  channels: OverviewChannel[];
+}
+
+// Live YouTube Analytics v2 deep path. Actual JSON (verified against YouTubeDeepAnalyticsDto):
+// four labeled-series arrays, each element = { metric, points: [{ day, value }] }.
+export interface YouTubeMetricPoint {
+  day: string;
+  value: number;
+}
+
+export interface YouTubeMetricSeries {
+  metric: string;
+  points: YouTubeMetricPoint[];
+}
+
+export interface YouTubeDeepAnalytics {
+  daySeries: YouTubeMetricSeries[];
+  trafficSources: YouTubeMetricSeries[];
+  geography: YouTubeMetricSeries[];
+  demographics: YouTubeMetricSeries[];
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.spec.ts
new file mode 100644
index 0000000..72ddb1b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.spec.ts
@@ -0,0 +1,65 @@
+import { TestBed } from '@angular/core/testing';
+import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
+import { OverviewComponent } from './overview.component';
+import { AnalyticsOverview } from '../models/channel-analytics.model';
+
+describe('OverviewComponent', () => {
+  let httpMock: HttpTestingController;
+
+  const stub: AnalyticsOverview = {
+    totalAudience: 45678,
+    combinedKpis: [
+      { key: 'audience', label: 'Total Audience', value: 45678, deltaPct: 3.2, rate: null },
+    ],
+    channels: [
+      {
+        platform: 'YouTube',
+        status: 'Connected',
+        followers: 12000,
+        followerSparkline: [
+          { date: '2026-07-01', value: 11800 },
+          { date: '2026-07-02', value: 11900 },
+          { date: '2026-07-03', value: 12000 },
+        ],
+      },
+      {
+        platform: 'Instagram',
+        status: 'ReconnectRequired',
+        followers: 33678,
+        followerSparkline: [
+          { date: '2026-07-01', value: 33000 },
+          { date: '2026-07-02', value: 33678 },
+        ],
+      },
+    ],
+  };
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [OverviewComponent, HttpClientTestingModule],
+    });
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => httpMock.verify());
+
+  it('renders the total audience labeled approximate and one sparkline per channel', () => {
+    const fixture = TestBed.createComponent(OverviewComponent);
+    fixture.detectChanges();
+
+    httpMock.expectOne('/api/analytics/overview?period=30d').flush(stub);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    const text = (el.textContent ?? '').toLowerCase();
+
+    // Total audience value present…
+    expect(text).toContain('45,678');
+    // …and explicitly labeled approximate.
+    expect(/approximate|approx|≈/.test(text)).toBeTrue();
+
+    // One sparkline (p-chart) per channel.
+    const charts = el.querySelectorAll('p-chart');
+    expect(charts.length).toBe(stub.channels.length);
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.ts
new file mode 100644
index 0000000..bb8dc06
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/overview/overview.component.ts
@@ -0,0 +1,219 @@
+import { Component, OnInit, computed, signal } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { ChartModule } from 'primeng/chart';
+import { SelectButtonModule } from 'primeng/selectbutton';
+import { FormsModule } from '@angular/forms';
+import { AnalyticsService } from '../services/analytics.service';
+import { AnalyticsPeriod } from '../models/analytics.model';
+import { AnalyticsOverview, OverviewChannel } from '../models/channel-analytics.model';
+
+// Line/accent colours from the obsidian theme accent ramp — one per platform sparkline.
+const SPARK_COLORS = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24'];
+
+@Component({
+  selector: 'app-overview',
+  standalone: true,
+  imports: [CommonModule, FormsModule, ChartModule, SelectButtonModule],
+  template: `
+    <div class="overview">
+      <header class="page-head">
+        <div class="page-head-text">
+          <h1 class="page-title">Overview</h1>
+          <p class="page-sub">All channels · last {{ periodLabel() }}</p>
+        </div>
+        <p-selectButton
+          class="period-select"
+          [options]="periodOptions"
+          [ngModel]="period()"
+          (ngModelChange)="changePeriod($event)"
+          optionLabel="label" optionValue="value" />
+      </header>
+
+      @if (loading()) {
+        <div class="total-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
+        <div class="kpi-grid">
+          @for (i of skeletonCells; track i) {
+            <div class="kpi-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
+          }
+        </div>
+      } @else {
+        @if (data(); as d) {
+        <section class="total-card">
+          <span class="total-label">Total Audience <span class="approx-tag" title="Approximate — YouTube rounds subscriber counts">(approximate)</span></span>
+          <span class="total-value">{{ d.totalAudience | number }}</span>
+        </section>
+
+        @if (d.combinedKpis.length) {
+          <section class="kpi-grid">
+            @for (k of d.combinedKpis; track k.key) {
+              <div class="kpi-card">
+                <span class="kpi-label">{{ k.label }}</span>
+                <span class="kpi-value">{{ k.value | number }}</span>
+                @if (k.deltaPct !== null) {
+                  <span class="kpi-delta" [class.up]="k.deltaPct >= 0" [class.down]="k.deltaPct < 0">
+                    {{ k.deltaPct >= 0 ? '▲' : '▼' }} {{ absPct(k.deltaPct) | number:'1.0-1' }}%
+                  </span>
+                }
+              </div>
+            }
+          </section>
+        }
+
+        <section class="channels">
+          @for (c of d.channels; track c.platform) {
+            <div class="channel-card">
+              <div class="channel-head">
+                <span class="channel-name">{{ c.platform }}</span>
+                <span class="status-pill" [class]="statusClass(c.status)">{{ statusLabel(c.status) }}</span>
+              </div>
+              <div class="channel-followers">
+                <span class="followers-value">{{ (c.followers ?? 0) | number }}</span>
+                <span class="followers-label">followers</span>
+              </div>
+              @if (c.followerSparkline.length) {
+                <div class="spark-wrap">
+                  <p-chart type="line" [data]="sparkData(c, $index)" [options]="sparkOptions" />
+                </div>
+              } @else {
+                <div class="spark-empty">No trend yet</div>
+              }
+            </div>
+          }
+        </section>
+        } @else {
+          <div class="empty empty-page">
+            <i class="pi pi-chart-bar"></i>
+            <p>No overview data available</p>
+          </div>
+        }
+      }
+    </div>
+  `,
+  styles: [`
+    :host { display: block; }
+    .overview { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }
+
+    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
+    .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
+    .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }
+
+    .total-card {
+      display: flex; flex-direction: column; gap: 6px;
+      background: var(--surface-card); border: 1px solid var(--surface-border);
+      border-radius: var(--r); padding: 20px 22px; margin-bottom: 18px;
+    }
+    .total-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
+    .approx-tag { text-transform: none; letter-spacing: 0; color: var(--text-muted); font-style: italic; }
+    .total-value { font-family: var(--font-mono); font-size: 36px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+
+    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
+    .kpi-card {
+      background: var(--surface-card); border: 1px solid var(--surface-border);
+      border-radius: var(--r); padding: 16px 18px; display: flex; flex-direction: column; gap: 8px;
+    }
+    .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
+    .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+    .kpi-delta { font-family: var(--font-mono); font-size: 12px; }
+    .kpi-delta.up { color: var(--status-approved, #4ade80); }
+    .kpi-delta.down { color: var(--status-rejected, #f0935f); }
+
+    .channels { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }
+    .channel-card { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 16px 18px; }
+    .channel-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 10px; }
+    .channel-name { font-family: var(--font-display); font-size: 16px; color: var(--text-primary); }
+    .status-pill { font-size: 10px; text-transform: uppercase; letter-spacing: .04em; padding: 2px 8px; border-radius: var(--r-pill); }
+    .status-pill.connected { background: color-mix(in srgb, var(--status-approved) 16%, transparent); color: var(--status-approved); }
+    .status-pill.reconnect { background: color-mix(in srgb, #fbbf24 16%, transparent); color: #fbbf24; }
+    .status-pill.disconnected { background: var(--surface-hover); color: var(--text-secondary); }
+    .channel-followers { display: flex; align-items: baseline; gap: 6px; margin-bottom: 12px; }
+    .followers-value { font-family: var(--font-mono); font-size: 22px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+    .followers-label { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--text-secondary); }
+    .spark-wrap { height: 60px; }
+    .spark-empty { height: 60px; display: grid; place-items: center; color: var(--text-muted); font-size: 12px; }
+
+    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 80px 16px; color: var(--text-muted); }
+    .empty i { font-size: 26px; }
+    .empty p { margin: 0; font-size: 13px; }
+
+    .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
+    .sk-line { height: 11px; width: 60%; margin-bottom: 8px; }
+    .sk-value { height: 26px; width: 80%; }
+    .skeleton-card { padding: 16px 18px; }
+    @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
+  `],
+})
+export class OverviewComponent implements OnInit {
+  readonly periodOptions = [
+    { label: '7d', value: '7d' as AnalyticsPeriod },
+    { label: '30d', value: '30d' as AnalyticsPeriod },
+    { label: '90d', value: '90d' as AnalyticsPeriod },
+  ];
+  readonly skeletonCells = [0, 1, 2, 3];
+
+  readonly period = signal<AnalyticsPeriod>('30d');
+  readonly loading = signal(false);
+  readonly data = signal<AnalyticsOverview | null>(null);
+
+  readonly sparkOptions = {
+    responsive: true,
+    maintainAspectRatio: false,
+    plugins: { legend: { display: false }, tooltip: { enabled: false } },
+    elements: { point: { radius: 0 } },
+    scales: { x: { display: false }, y: { display: false } },
+  };
+
+  readonly periodLabel = computed(() =>
+    ({ '7d': '7 days', '30d': '30 days', '90d': '90 days' })[this.period()]);
+
+  constructor(private readonly api: AnalyticsService) {}
+
+  ngOnInit(): void {
+    this.load();
+  }
+
+  changePeriod(p: AnalyticsPeriod): void {
+    this.period.set(p);
+    this.load();
+  }
+
+  absPct(delta: number): number {
+    return Math.abs(delta);
+  }
+
+  statusClass(status: OverviewChannel['status']): string {
+    return status === 'Connected' ? 'connected' : status === 'ReconnectRequired' ? 'reconnect' : 'disconnected';
+  }
+
+  statusLabel(status: OverviewChannel['status']): string {
+    return status === 'Connected' ? 'Connected' : status === 'ReconnectRequired' ? 'Reconnect' : 'Not connected';
+  }
+
+  sparkData(channel: OverviewChannel, index: number) {
+    const color = SPARK_COLORS[index % SPARK_COLORS.length];
+    return {
+      labels: channel.followerSparkline.map(p => p.date),
+      datasets: [{
+        data: channel.followerSparkline.map(p => p.value),
+        borderColor: color,
+        backgroundColor: color,
+        borderWidth: 2,
+        tension: 0.35,
+        fill: false,
+      }],
+    };
+  }
+
+  private load(): void {
+    this.loading.set(true);
+    this.api.getOverview(this.period()).subscribe({
+      next: d => {
+        this.data.set(d);
+        this.loading.set(false);
+      },
+      error: () => {
+        this.data.set(null);
+        this.loading.set(false);
+      },
+    });
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
index 4d27726..2b64951 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
@@ -40,4 +40,25 @@ describe('AnalyticsService', () => {
     expect(req.request.method).toBe('GET');
     req.flush({ ga4: true, searchConsole: true });
   });
+
+  it('requests overview with the period param', () => {
+    service.getOverview('7d').subscribe();
+    const req = httpMock.expectOne('/api/analytics/overview?period=7d');
+    expect(req.request.method).toBe('GET');
+    req.flush({ totalAudience: 0, combinedKpis: [], channels: [] });
+  });
+
+  it('requests channel analytics for a platform with the period param', () => {
+    service.getChannel('youtube', '30d').subscribe();
+    const req = httpMock.expectOne('/api/analytics/channel/youtube?period=30d');
+    expect(req.request.method).toBe('GET');
+    req.flush({ platform: 'YouTube', status: 'Connected', asOf: null, kpis: [], trends: [], recentPosts: [] });
+  });
+
+  it('requests the YouTube deep path with the period param', () => {
+    service.getYouTubeDeep('90d').subscribe();
+    const req = httpMock.expectOne('/api/analytics/youtube/deep?period=90d');
+    expect(req.request.method).toBe('GET');
+    req.flush({ daySeries: [], trafficSources: [], geography: [], demographics: [] });
+  });
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
index 3686c63..717e78b 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts
@@ -2,6 +2,12 @@ import { Injectable } from '@angular/core';
 import { HttpClient, HttpParams } from '@angular/common/http';
 import { Observable } from 'rxjs';
 import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from '../models/analytics.model';
+import {
+  AnalyticsOverview,
+  ChannelAnalytics,
+  ChannelPlatform,
+  YouTubeDeepAnalytics,
+} from '../models/channel-analytics.model';
 
 @Injectable({ providedIn: 'root' })
 export class AnalyticsService {
@@ -17,4 +23,19 @@ export class AnalyticsService {
   getHealth(): Observable<AnalyticsHealth> {
     return this.http.get<AnalyticsHealth>(`${this.baseUrl}/health`);
   }
+
+  getOverview(period: AnalyticsPeriod): Observable<AnalyticsOverview> {
+    const params = new HttpParams().set('period', period);
+    return this.http.get<AnalyticsOverview>(`${this.baseUrl}/overview`, { params });
+  }
+
+  getChannel(platform: ChannelPlatform, period: AnalyticsPeriod): Observable<ChannelAnalytics> {
+    const params = new HttpParams().set('period', period);
+    return this.http.get<ChannelAnalytics>(`${this.baseUrl}/channel/${platform}`, { params });
+  }
+
+  getYouTubeDeep(period: AnalyticsPeriod): Observable<YouTubeDeepAnalytics> {
+    const params = new HttpParams().set('period', period);
+    return this.http.get<YouTubeDeepAnalytics>(`${this.baseUrl}/youtube/deep`, { params });
+  }
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.spec.ts
new file mode 100644
index 0000000..cfc2ac9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.spec.ts
@@ -0,0 +1,52 @@
+import { TestBed } from '@angular/core/testing';
+import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
+import { WebsiteAnalyticsComponent } from './website-analytics.component';
+import { WebsiteAnalytics } from '../models/analytics.model';
+
+// Regression: this markup/behaviour moved verbatim out of AnalyticsComponent. These are the two
+// original Website assertions, re-pointed at the component that now owns them.
+describe('WebsiteAnalyticsComponent', () => {
+  let httpMock: HttpTestingController;
+
+  const stub: WebsiteAnalytics = {
+    overview: { activeUsers: 123, sessions: 200, pageViews: 500, avgSessionDuration: 90, bounceRate: 0.4, newUsers: 80 },
+    topPages: [{ pagePath: '/blog', views: 50, uniqueUsers: 30 }],
+    trafficSources: [{ channel: 'Organic Search', sessions: 100, users: 80 }],
+    searchQueries: [{ query: 'ai tools', clicks: 50, impressions: 1000, ctr: 0.05, position: 3.2 }],
+  };
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [WebsiteAnalyticsComponent, HttpClientTestingModule],
+    });
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => httpMock.verify());
+
+  it('loads website analytics on init and renders active users', () => {
+    const fixture = TestBed.createComponent(WebsiteAnalyticsComponent);
+    fixture.detectChanges();
+
+    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
+    const health = httpMock.match('/api/analytics/health');
+    health.forEach(r => r.flush({ ga4: true, searchConsole: true }));
+
+    fixture.detectChanges();
+    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
+    expect(text).toContain('123');
+  });
+
+  it('shows an unavailable banner when a health source is down', () => {
+    const fixture = TestBed.createComponent(WebsiteAnalyticsComponent);
+    fixture.detectChanges();
+
+    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
+    const health = httpMock.match('/api/analytics/health');
+    health.forEach(r => r.flush({ ga4: false, searchConsole: true }));
+
+    fixture.detectChanges();
+    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
+    expect(text).toContain('unavailable');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.ts
new file mode 100644
index 0000000..547d85c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/website/website-analytics.component.ts
@@ -0,0 +1,417 @@
+import { Component, OnInit, computed, signal } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { TableModule } from 'primeng/table';
+import { ChartModule } from 'primeng/chart';
+import { SelectButtonModule } from 'primeng/selectbutton';
+import { TooltipModule } from 'primeng/tooltip';
+import { FormsModule } from '@angular/forms';
+import { AnalyticsService } from '../services/analytics.service';
+import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from '../models/analytics.model';
+
+interface Kpi {
+  readonly icon: string;
+  readonly label: string;
+  readonly value: string;
+  readonly desc: string;
+}
+
+interface LegendRow {
+  readonly channel: string;
+  readonly sessions: number;
+  readonly pct: number;
+  readonly color: string;
+}
+
+// Channel palette drawn from the app's status/accent tokens (obsidian theme).
+const TRAFFIC_COLORS = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24', '#5a5a66', '#f0935f'];
+
+@Component({
+  selector: 'app-website-analytics',
+  standalone: true,
+  imports: [CommonModule, FormsModule, TableModule, ChartModule, SelectButtonModule, TooltipModule],
+  template: `
+    <div class="analytics">
+      <header class="page-head">
+        <div class="page-head-text">
+          <h1 class="page-title">Website Analytics</h1>
+          <p class="page-sub">matthewkruczek.ai · last {{ periodLabel() }}</p>
+        </div>
+        <p-selectButton
+          class="period-select"
+          [options]="periodOptions"
+          [ngModel]="period()"
+          (ngModelChange)="changePeriod($event)"
+          optionLabel="label" optionValue="value" />
+      </header>
+
+      @if (health(); as h) {
+        @if (!h.ga4 || !h.searchConsole) {
+          <div class="banner" role="status">
+            <i class="pi pi-exclamation-triangle"></i>
+            <span>
+              Some analytics sources are unavailable
+              @if (!h.ga4) { <strong> · Google Analytics</strong> }
+              @if (!h.searchConsole) { <strong> · Search Console</strong> }
+            </span>
+          </div>
+        }
+      }
+
+      @if (loading()) {
+        <div class="kpi-grid">
+          @for (i of skeletonCells; track i) {
+            <div class="kpi-card skeleton-card"><div class="sk sk-icon"></div><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
+          }
+        </div>
+        <div class="panels">
+          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
+          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
+        </div>
+      } @else if (data()) {
+        @if (data(); as d) {
+        <section class="kpi-grid">
+          @for (k of kpis(); track k.label) {
+            <div class="kpi-card">
+              <div class="kpi-top">
+                <span class="kpi-icon"><i class="pi {{ k.icon }}"></i></span>
+                <span class="kpi-label">{{ k.label }}</span>
+                <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
+                   [attr.aria-label]="k.label + ': ' + k.desc"
+                   [pTooltip]="k.desc" tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
+              </div>
+              <div class="kpi-value">{{ k.value }}</div>
+            </div>
+          }
+        </section>
+
+        <section class="panels">
+          <div class="panel">
+            <div class="panel-head">
+              <h2>Traffic Sources</h2>
+              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
+                 aria-label="Traffic Sources: how visitors arrived"
+                 pTooltip="How visitors arrived, grouped by channel — direct, referral, organic search, social (Google Analytics)."
+                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
+            </div>
+            @if (d.trafficSources.length) {
+              <div class="doughnut-wrap">
+                <p-chart type="doughnut" [data]="trafficData()" [options]="chartOptions" />
+                <div class="doughnut-center">
+                  <span class="dc-value">{{ totalSessions() | number }}</span>
+                  <span class="dc-label">sessions</span>
+                </div>
+              </div>
+              <ul class="legend">
+                @for (row of trafficLegend(); track row.channel) {
+                  <li>
+                    <span class="dot" [style.background]="row.color"></span>
+                    <span class="legend-name">{{ row.channel }}</span>
+                    <span class="legend-val">{{ row.sessions | number }}</span>
+                    <span class="legend-pct">{{ row.pct | number:'1.0-0' }}%</span>
+                  </li>
+                }
+              </ul>
+            } @else {
+              <div class="empty"><i class="pi pi-chart-pie"></i><p>No traffic data</p></div>
+            }
+          </div>
+
+          <div class="panel">
+            <div class="panel-head">
+              <h2>Top Pages</h2>
+              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
+                 aria-label="Top Pages: most-viewed pages"
+                 pTooltip="Your most-viewed pages this period, with total views and the unique visitors who saw each (Google Analytics)."
+                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
+            </div>
+            @if (d.topPages.length) {
+              <table class="data-table">
+                <thead><tr><th>Page</th><th class="num">Views</th><th class="num">Users</th></tr></thead>
+                <tbody>
+                  @for (row of d.topPages; track row.pagePath) {
+                    <tr>
+                      <td class="path" [title]="row.pagePath">
+                        <span class="path-text">{{ row.pagePath }}</span>
+                        <span class="bar"><span class="bar-fill" [style.width.%]="barWidth(row.views)"></span></span>
+                      </td>
+                      <td class="num strong">{{ row.views | number }}</td>
+                      <td class="num">{{ row.uniqueUsers | number }}</td>
+                    </tr>
+                  }
+                </tbody>
+              </table>
+            } @else {
+              <div class="empty"><i class="pi pi-file"></i><p>No page data</p></div>
+            }
+          </div>
+        </section>
+
+        <section class="panel">
+          <div class="panel-head">
+            <h2>Top Search Queries</h2>
+            <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
+               aria-label="Top Search Queries: Google searches where the site appeared"
+               pTooltip="Google searches where your site appeared. Impressions = times shown, Clicks = visits from search, CTR = click rate, Position = average rank (lower is better). Source: Search Console."
+               tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
+          </div>
+          @if (d.searchQueries.length) {
+            <table class="data-table queries">
+              <thead>
+                <tr><th>Query</th><th class="num">Clicks</th><th class="num">Impr.</th><th class="num">CTR</th><th class="num">Position</th></tr>
+              </thead>
+              <tbody>
+                @for (row of d.searchQueries; track row.query) {
+                  <tr>
+                    <td class="query" [title]="row.query">{{ row.query }}</td>
+                    <td class="num strong">{{ row.clicks | number }}</td>
+                    <td class="num">{{ row.impressions | number }}</td>
+                    <td class="num">{{ (row.ctr * 100) | number:'1.0-1' }}%</td>
+                    <td class="num"><span class="pos" [class]="posClass(row.position)">{{ row.position | number:'1.0-1' }}</span></td>
+                  </tr>
+                }
+              </tbody>
+            </table>
+          } @else {
+            <div class="empty"><i class="pi pi-search"></i><p>No search queries</p></div>
+          }
+        </section>
+        }
+      } @else {
+        <div class="empty empty-page">
+          <i class="pi pi-chart-bar"></i>
+          <p>No analytics data available</p>
+        </div>
+      }
+    </div>
+  `,
+  styles: [`
+    :host { display: block; }
+    .analytics { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }
+
+    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
+    .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
+    .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }
+
+    .banner {
+      display: flex; align-items: center; gap: 10px;
+      background: var(--delivery-warn-bg); color: var(--delivery-warn-fg);
+      border: 1px solid color-mix(in srgb, var(--delivery-warn-fg) 30%, transparent);
+      border-radius: var(--r-inner); padding: 11px 14px; margin-bottom: 20px; font-size: 13px;
+    }
+    .banner strong { font-weight: 600; }
+
+    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
+    .kpi-card {
+      background: var(--surface-card); border: 1px solid var(--surface-border);
+      border-radius: var(--r); padding: 16px 18px;
+      display: flex; flex-direction: column; gap: 12px;
+      transition: border-color .14s, box-shadow .14s;
+    }
+    .kpi-card:hover { border-color: var(--surface-disabled); box-shadow: 0 6px 20px -12px rgba(0,0,0,.6); }
+    .kpi-top { display: flex; align-items: center; gap: 10px; }
+    .kpi-top .kpi-label { flex: 1; }
+    .info-icon {
+      font-size: 13px; color: var(--text-secondary); cursor: help;
+      transition: color .14s; border-radius: 99px; outline: none;
+    }
+    .info-icon:hover, .info-icon:focus-visible { color: var(--brand-primary); }
+    .info-icon:focus-visible { box-shadow: 0 0 0 2px color-mix(in srgb, var(--brand-primary) 50%, transparent); }
+    .kpi-icon {
+      width: 34px; height: 34px; border-radius: var(--r-control);
+      background: var(--accent-soft); color: var(--brand-primary);
+      display: grid; place-items: center; font-size: 15px; flex-shrink: 0;
+    }
+    .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
+    .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+
+    .panels { display: grid; grid-template-columns: minmax(300px, 5fr) minmax(0, 7fr); gap: 14px; margin-bottom: 14px; }
+    @media (max-width: 880px) { .panels { grid-template-columns: 1fr; } }
+
+    .panel { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 18px 20px; }
+    .panel-head { display: flex; align-items: center; gap: 7px; margin-bottom: 14px; }
+    .panel-head h2 { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); margin: 0; }
+
+    .doughnut-wrap { position: relative; height: 200px; margin-bottom: 12px; }
+    .doughnut-center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
+    .dc-value { font-family: var(--font-mono); font-size: 22px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
+    .dc-label { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--text-secondary); }
+
+    .legend { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
+    .legend li { display: flex; align-items: center; gap: 10px; font-size: 13px; }
+    .dot { width: 9px; height: 9px; border-radius: 99px; flex-shrink: 0; }
+    .legend-name { color: var(--text-primary); flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
+    .legend-val { font-family: var(--font-mono); color: var(--text-primary); font-variant-numeric: tabular-nums; }
+    .legend-pct { font-family: var(--font-mono); color: var(--text-secondary); min-width: 36px; text-align: right; font-variant-numeric: tabular-nums; }
+
+    .data-table { width: 100%; border-collapse: collapse; font-size: 13px; }
+    .data-table th {
+      text-align: left; font-weight: 500; color: var(--text-secondary);
+      font-size: 11px; letter-spacing: .04em; text-transform: uppercase;
+      padding: 8px 12px; border-bottom: 1px solid var(--surface-border);
+    }
+    .data-table td { padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 55%, transparent); color: var(--text-primary); }
+    .data-table tbody tr:last-child td { border-bottom: 0; }
+    .data-table tbody tr { transition: background .12s; }
+    .data-table tbody tr:hover { background: var(--surface-hover); }
+    .num { text-align: right; font-family: var(--font-mono); font-variant-numeric: tabular-nums; white-space: nowrap; }
+    th.num { text-align: right; }
+    .strong { color: var(--text-primary); font-weight: 600; }
+    .data-table td.num:not(.strong) { color: var(--text-secondary); }
+
+    .path { max-width: 0; }
+    .path-text { display: block; font-family: var(--font-mono); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
+    .bar { display: block; height: 3px; margin-top: 6px; background: var(--surface-hover); border-radius: 99px; overflow: hidden; }
+    .bar-fill { display: block; height: 100%; background: var(--brand-primary); border-radius: 99px; }
+    .query { max-width: 320px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
+
+    .pos { font-family: var(--font-mono); padding: 2px 8px; border-radius: var(--r-pill); font-size: 12px; }
+    .pos.good { background: color-mix(in srgb, var(--status-approved) 16%, transparent); color: var(--status-approved); }
+    .pos.mid { background: color-mix(in srgb, var(--score-warning, #fbbf24) 16%, transparent); color: #fbbf24; }
+    .pos.low { background: var(--surface-hover); color: var(--text-secondary); }
+
+    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 36px 16px; color: var(--text-muted); }
+    .empty i { font-size: 26px; }
+    .empty p { margin: 0; font-size: 13px; }
+    .empty-page { padding: 80px 16px; }
+
+    .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
+    .sk-icon { width: 34px; height: 34px; }
+    .sk-line { height: 11px; width: 60%; }
+    .sk-value { height: 22px; width: 80%; }
+    .skeleton-panel .sk-block { height: 200px; width: 100%; border-radius: var(--r-inner); }
+    @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
+  `],
+})
+export class WebsiteAnalyticsComponent implements OnInit {
+  readonly periodOptions = [
+    { label: '7d', value: '7d' as AnalyticsPeriod },
+    { label: '30d', value: '30d' as AnalyticsPeriod },
+    { label: '90d', value: '90d' as AnalyticsPeriod },
+  ];
+  readonly skeletonCells = [0, 1, 2, 3, 4, 5];
+
+  readonly period = signal<AnalyticsPeriod>('30d');
+  readonly loading = signal(false);
+  readonly data = signal<WebsiteAnalytics | null>(null);
+  readonly health = signal<AnalyticsHealth | null>(null);
+
+  readonly chartOptions = {
+    cutout: '70%',
+    responsive: true,
+    maintainAspectRatio: false,
+    plugins: {
+      legend: { display: false },
+      tooltip: {
+        backgroundColor: '#1a1a20',
+        borderColor: '#2c2c36',
+        borderWidth: 1,
+        titleColor: '#f0f0f5',
+        bodyColor: '#8a8a96',
+        padding: 10,
+        cornerRadius: 8,
+      },
+    },
+  };
+
+  readonly periodLabel = computed(() =>
+    ({ '7d': '7 days', '30d': '30 days', '90d': '90 days' })[this.period()]);
+
+  readonly totalSessions = computed(() =>
+    (this.data()?.trafficSources ?? []).reduce((sum, s) => sum + s.sessions, 0));
+
+  readonly trafficLegend = computed<LegendRow[]>(() => {
+    const sources = this.data()?.trafficSources ?? [];
+    const total = this.totalSessions() || 1;
+    return sources.map((s, i) => ({
+      channel: s.channel,
+      sessions: s.sessions,
+      pct: (s.sessions / total) * 100,
+      color: TRAFFIC_COLORS[i % TRAFFIC_COLORS.length],
+    }));
+  });
+
+  readonly trafficData = computed(() => {
+    const sources = this.data()?.trafficSources ?? [];
+    return {
+      labels: sources.map(s => s.channel),
+      datasets: [{
+        data: sources.map(s => s.sessions),
+        backgroundColor: sources.map((_, i) => TRAFFIC_COLORS[i % TRAFFIC_COLORS.length]),
+        borderColor: '#141418',
+        borderWidth: 2,
+        hoverOffset: 4,
+      }],
+    };
+  });
+
+  readonly kpis = computed<Kpi[]>(() => {
+    const o = this.data()?.overview;
+    if (!o) return [];
+    return [
+      { icon: 'pi-users', label: 'Users', value: this.num(o.activeUsers),
+        desc: 'Distinct people who visited the site in this period. One person is counted once no matter how many times they return (GA4 active users).' },
+      { icon: 'pi-chart-line', label: 'Sessions', value: this.num(o.sessions),
+        desc: 'Individual visits to the site. A single person can start several sessions, so this is usually higher than Users (GA4).' },
+      { icon: 'pi-eye', label: 'Page Views', value: this.num(o.pageViews),
+        desc: 'Total pages loaded, including repeat views of the same page. Measures overall content consumption (GA4).' },
+      { icon: 'pi-user-plus', label: 'New Users', value: this.num(o.newUsers),
+        desc: 'First-time visitors who had never been to the site before this period (GA4).' },
+      { icon: 'pi-percentage', label: 'Bounce Rate', value: `${(o.bounceRate * 100).toFixed(1)}%`,
+        desc: 'Share of sessions where the visitor left without any meaningful interaction. Lower is better (GA4).' },
+      { icon: 'pi-clock', label: 'Avg Session', value: this.duration(o.avgSessionDuration),
+        desc: 'Average time a visitor spent on the site per session. Longer sessions suggest more engaging content (GA4).' },
+    ];
+  });
+
+  private readonly maxPageViews = computed(() =>
+    Math.max(1, ...(this.data()?.topPages ?? []).map(p => p.views)));
+
+  constructor(private readonly api: AnalyticsService) {}
+
+  ngOnInit(): void {
+    this.load();
+    this.api.getHealth().subscribe({
+      next: h => this.health.set(h),
+      error: () => this.health.set({ ga4: false, searchConsole: false }),
+    });
+  }
+
+  changePeriod(p: AnalyticsPeriod): void {
+    this.period.set(p);
+    this.load();
+  }
+
+  barWidth(views: number): number {
+    return (views / this.maxPageViews()) * 100;
+  }
+
+  posClass(position: number): string {
+    if (position <= 10) return 'good';
+    if (position <= 30) return 'mid';
+    return 'low';
+  }
+
+  private num(value: number): string {
+    return value.toLocaleString('en-US');
+  }
+
+  private duration(seconds: number): string {
+    const m = Math.floor(seconds / 60);
+    const s = Math.round(seconds % 60);
+    return m > 0 ? `${m}m ${s}s` : `${s}s`;
+  }
+
+  private load(): void {
+    this.loading.set(true);
+    this.api.getWebsite(this.period()).subscribe({
+      next: d => {
+        this.data.set(d);
+        this.loading.set(false);
+      },
+      error: () => {
+        this.data.set(null);
+        this.loading.set(false);
+      },
+    });
+  }
+}
