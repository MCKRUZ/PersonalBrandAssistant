import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { ChartModule } from 'primeng/chart';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TooltipModule } from 'primeng/tooltip';
import { FormsModule } from '@angular/forms';
import { AnalyticsService } from './services/analytics.service';
import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from './models/analytics.model';

interface Kpi {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly desc: string;
}

interface LegendRow {
  readonly channel: string;
  readonly sessions: number;
  readonly pct: number;
  readonly color: string;
}

// Channel palette drawn from the app's status/accent tokens (obsidian theme).
const TRAFFIC_COLORS = ['#c87156', '#8a7df0', '#60a5fa', '#4ade80', '#fbbf24', '#5a5a66', '#f0935f'];

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, TableModule, ChartModule, SelectButtonModule, TooltipModule],
  template: `
    <div class="analytics">
      <header class="page-head">
        <div class="page-head-text">
          <h1 class="page-title">Website Analytics</h1>
          <p class="page-sub">matthewkruczek.ai · last {{ periodLabel() }}</p>
        </div>
        <p-selectButton
          class="period-select"
          [options]="periodOptions"
          [ngModel]="period()"
          (ngModelChange)="changePeriod($event)"
          optionLabel="label" optionValue="value" />
      </header>

      @if (health(); as h) {
        @if (!h.ga4 || !h.searchConsole) {
          <div class="banner" role="status">
            <i class="pi pi-exclamation-triangle"></i>
            <span>
              Some analytics sources are unavailable
              @if (!h.ga4) { <strong> · Google Analytics</strong> }
              @if (!h.searchConsole) { <strong> · Search Console</strong> }
            </span>
          </div>
        }
      }

      @if (loading()) {
        <div class="kpi-grid">
          @for (i of skeletonCells; track i) {
            <div class="kpi-card skeleton-card"><div class="sk sk-icon"></div><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
          }
        </div>
        <div class="panels">
          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
          <div class="panel skeleton-panel"><div class="sk sk-block"></div></div>
        </div>
      } @else if (data()) {
        @if (data(); as d) {
        <section class="kpi-grid">
          @for (k of kpis(); track k.label) {
            <div class="kpi-card">
              <div class="kpi-top">
                <span class="kpi-icon"><i class="pi {{ k.icon }}"></i></span>
                <span class="kpi-label">{{ k.label }}</span>
                <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
                   [attr.aria-label]="k.label + ': ' + k.desc"
                   [pTooltip]="k.desc" tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
              </div>
              <div class="kpi-value">{{ k.value }}</div>
            </div>
          }
        </section>

        <section class="panels">
          <div class="panel">
            <div class="panel-head">
              <h2>Traffic Sources</h2>
              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
                 aria-label="Traffic Sources: how visitors arrived"
                 pTooltip="How visitors arrived, grouped by channel — direct, referral, organic search, social (Google Analytics)."
                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
            </div>
            @if (d.trafficSources.length) {
              <div class="doughnut-wrap">
                <p-chart type="doughnut" [data]="trafficData()" [options]="chartOptions" />
                <div class="doughnut-center">
                  <span class="dc-value">{{ totalSessions() | number }}</span>
                  <span class="dc-label">sessions</span>
                </div>
              </div>
              <ul class="legend">
                @for (row of trafficLegend(); track row.channel) {
                  <li>
                    <span class="dot" [style.background]="row.color"></span>
                    <span class="legend-name">{{ row.channel }}</span>
                    <span class="legend-val">{{ row.sessions | number }}</span>
                    <span class="legend-pct">{{ row.pct | number:'1.0-0' }}%</span>
                  </li>
                }
              </ul>
            } @else {
              <div class="empty"><i class="pi pi-chart-pie"></i><p>No traffic data</p></div>
            }
          </div>

          <div class="panel">
            <div class="panel-head">
              <h2>Top Pages</h2>
              <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
                 aria-label="Top Pages: most-viewed pages"
                 pTooltip="Your most-viewed pages this period, with total views and the unique visitors who saw each (Google Analytics)."
                 tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
            </div>
            @if (d.topPages.length) {
              <table class="data-table">
                <thead><tr><th>Page</th><th class="num">Views</th><th class="num">Users</th></tr></thead>
                <tbody>
                  @for (row of d.topPages; track row.pagePath) {
                    <tr>
                      <td class="path" [title]="row.pagePath">
                        <span class="path-text">{{ row.pagePath }}</span>
                        <span class="bar"><span class="bar-fill" [style.width.%]="barWidth(row.views)"></span></span>
                      </td>
                      <td class="num strong">{{ row.views | number }}</td>
                      <td class="num">{{ row.uniqueUsers | number }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <div class="empty"><i class="pi pi-file"></i><p>No page data</p></div>
            }
          </div>
        </section>

        <section class="panel">
          <div class="panel-head">
            <h2>Top Search Queries</h2>
            <i class="pi pi-info-circle info-icon" tabindex="0" role="button"
               aria-label="Top Search Queries: Google searches where the site appeared"
               pTooltip="Google searches where your site appeared. Impressions = times shown, Clicks = visits from search, CTR = click rate, Position = average rank (lower is better). Source: Search Console."
               tooltipPosition="top" [tooltipStyleClass]="'analytics-tip'"></i>
          </div>
          @if (d.searchQueries.length) {
            <table class="data-table queries">
              <thead>
                <tr><th>Query</th><th class="num">Clicks</th><th class="num">Impr.</th><th class="num">CTR</th><th class="num">Position</th></tr>
              </thead>
              <tbody>
                @for (row of d.searchQueries; track row.query) {
                  <tr>
                    <td class="query" [title]="row.query">{{ row.query }}</td>
                    <td class="num strong">{{ row.clicks | number }}</td>
                    <td class="num">{{ row.impressions | number }}</td>
                    <td class="num">{{ (row.ctr * 100) | number:'1.0-1' }}%</td>
                    <td class="num"><span class="pos" [class]="posClass(row.position)">{{ row.position | number:'1.0-1' }}</span></td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <div class="empty"><i class="pi pi-search"></i><p>No search queries</p></div>
          }
        </section>
        }
      } @else {
        <div class="empty empty-page">
          <i class="pi pi-chart-bar"></i>
          <p>No analytics data available</p>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .analytics { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }

    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
    .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
    .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }

    .banner {
      display: flex; align-items: center; gap: 10px;
      background: var(--delivery-warn-bg); color: var(--delivery-warn-fg);
      border: 1px solid color-mix(in srgb, var(--delivery-warn-fg) 30%, transparent);
      border-radius: var(--r-inner); padding: 11px 14px; margin-bottom: 20px; font-size: 13px;
    }
    .banner strong { font-weight: 600; }

    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
    .kpi-card {
      background: var(--surface-card); border: 1px solid var(--surface-border);
      border-radius: var(--r); padding: 16px 18px;
      display: flex; flex-direction: column; gap: 12px;
      transition: border-color .14s, box-shadow .14s;
    }
    .kpi-card:hover { border-color: var(--surface-disabled); box-shadow: 0 6px 20px -12px rgba(0,0,0,.6); }
    .kpi-top { display: flex; align-items: center; gap: 10px; }
    .kpi-top .kpi-label { flex: 1; }
    .info-icon {
      font-size: 13px; color: var(--text-secondary); cursor: help;
      transition: color .14s; border-radius: 99px; outline: none;
    }
    .info-icon:hover, .info-icon:focus-visible { color: var(--brand-primary); }
    .info-icon:focus-visible { box-shadow: 0 0 0 2px color-mix(in srgb, var(--brand-primary) 50%, transparent); }
    .kpi-icon {
      width: 34px; height: 34px; border-radius: var(--r-control);
      background: var(--accent-soft); color: var(--brand-primary);
      display: grid; place-items: center; font-size: 15px; flex-shrink: 0;
    }
    .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
    .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }

    .panels { display: grid; grid-template-columns: minmax(300px, 5fr) minmax(0, 7fr); gap: 14px; margin-bottom: 14px; }
    @media (max-width: 880px) { .panels { grid-template-columns: 1fr; } }

    .panel { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 18px 20px; }
    .panel-head { display: flex; align-items: center; gap: 7px; margin-bottom: 14px; }
    .panel-head h2 { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); margin: 0; }

    .doughnut-wrap { position: relative; height: 200px; margin-bottom: 12px; }
    .doughnut-center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
    .dc-value { font-family: var(--font-mono); font-size: 22px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
    .dc-label { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--text-secondary); }

    .legend { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .legend li { display: flex; align-items: center; gap: 10px; font-size: 13px; }
    .dot { width: 9px; height: 9px; border-radius: 99px; flex-shrink: 0; }
    .legend-name { color: var(--text-primary); flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .legend-val { font-family: var(--font-mono); color: var(--text-primary); font-variant-numeric: tabular-nums; }
    .legend-pct { font-family: var(--font-mono); color: var(--text-secondary); min-width: 36px; text-align: right; font-variant-numeric: tabular-nums; }

    .data-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .data-table th {
      text-align: left; font-weight: 500; color: var(--text-secondary);
      font-size: 11px; letter-spacing: .04em; text-transform: uppercase;
      padding: 8px 12px; border-bottom: 1px solid var(--surface-border);
    }
    .data-table td { padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 55%, transparent); color: var(--text-primary); }
    .data-table tbody tr:last-child td { border-bottom: 0; }
    .data-table tbody tr { transition: background .12s; }
    .data-table tbody tr:hover { background: var(--surface-hover); }
    .num { text-align: right; font-family: var(--font-mono); font-variant-numeric: tabular-nums; white-space: nowrap; }
    th.num { text-align: right; }
    .strong { color: var(--text-primary); font-weight: 600; }
    .data-table td.num:not(.strong) { color: var(--text-secondary); }

    .path { max-width: 0; }
    .path-text { display: block; font-family: var(--font-mono); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .bar { display: block; height: 3px; margin-top: 6px; background: var(--surface-hover); border-radius: 99px; overflow: hidden; }
    .bar-fill { display: block; height: 100%; background: var(--brand-primary); border-radius: 99px; }
    .query { max-width: 320px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .pos { font-family: var(--font-mono); padding: 2px 8px; border-radius: var(--r-pill); font-size: 12px; }
    .pos.good { background: color-mix(in srgb, var(--status-approved) 16%, transparent); color: var(--status-approved); }
    .pos.mid { background: color-mix(in srgb, var(--score-warning, #fbbf24) 16%, transparent); color: #fbbf24; }
    .pos.low { background: var(--surface-hover); color: var(--text-secondary); }

    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 36px 16px; color: var(--text-muted); }
    .empty i { font-size: 26px; }
    .empty p { margin: 0; font-size: 13px; }
    .empty-page { padding: 80px 16px; }

    .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
    .sk-icon { width: 34px; height: 34px; }
    .sk-line { height: 11px; width: 60%; }
    .sk-value { height: 22px; width: 80%; }
    .skeleton-panel .sk-block { height: 200px; width: 100%; border-radius: var(--r-inner); }
    @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
  `],
})
export class AnalyticsComponent implements OnInit {
  readonly periodOptions = [
    { label: '7d', value: '7d' as AnalyticsPeriod },
    { label: '30d', value: '30d' as AnalyticsPeriod },
    { label: '90d', value: '90d' as AnalyticsPeriod },
  ];
  readonly skeletonCells = [0, 1, 2, 3, 4, 5];

  readonly period = signal<AnalyticsPeriod>('30d');
  readonly loading = signal(false);
  readonly data = signal<WebsiteAnalytics | null>(null);
  readonly health = signal<AnalyticsHealth | null>(null);

  readonly chartOptions = {
    cutout: '70%',
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#1a1a20',
        borderColor: '#2c2c36',
        borderWidth: 1,
        titleColor: '#f0f0f5',
        bodyColor: '#8a8a96',
        padding: 10,
        cornerRadius: 8,
      },
    },
  };

  readonly periodLabel = computed(() =>
    ({ '7d': '7 days', '30d': '30 days', '90d': '90 days' })[this.period()]);

  readonly totalSessions = computed(() =>
    (this.data()?.trafficSources ?? []).reduce((sum, s) => sum + s.sessions, 0));

  readonly trafficLegend = computed<LegendRow[]>(() => {
    const sources = this.data()?.trafficSources ?? [];
    const total = this.totalSessions() || 1;
    return sources.map((s, i) => ({
      channel: s.channel,
      sessions: s.sessions,
      pct: (s.sessions / total) * 100,
      color: TRAFFIC_COLORS[i % TRAFFIC_COLORS.length],
    }));
  });

  readonly trafficData = computed(() => {
    const sources = this.data()?.trafficSources ?? [];
    return {
      labels: sources.map(s => s.channel),
      datasets: [{
        data: sources.map(s => s.sessions),
        backgroundColor: sources.map((_, i) => TRAFFIC_COLORS[i % TRAFFIC_COLORS.length]),
        borderColor: '#141418',
        borderWidth: 2,
        hoverOffset: 4,
      }],
    };
  });

  readonly kpis = computed<Kpi[]>(() => {
    const o = this.data()?.overview;
    if (!o) return [];
    return [
      { icon: 'pi-users', label: 'Users', value: this.num(o.activeUsers),
        desc: 'Distinct people who visited the site in this period. One person is counted once no matter how many times they return (GA4 active users).' },
      { icon: 'pi-chart-line', label: 'Sessions', value: this.num(o.sessions),
        desc: 'Individual visits to the site. A single person can start several sessions, so this is usually higher than Users (GA4).' },
      { icon: 'pi-eye', label: 'Page Views', value: this.num(o.pageViews),
        desc: 'Total pages loaded, including repeat views of the same page. Measures overall content consumption (GA4).' },
      { icon: 'pi-user-plus', label: 'New Users', value: this.num(o.newUsers),
        desc: 'First-time visitors who had never been to the site before this period (GA4).' },
      { icon: 'pi-percentage', label: 'Bounce Rate', value: `${(o.bounceRate * 100).toFixed(1)}%`,
        desc: 'Share of sessions where the visitor left without any meaningful interaction. Lower is better (GA4).' },
      { icon: 'pi-clock', label: 'Avg Session', value: this.duration(o.avgSessionDuration),
        desc: 'Average time a visitor spent on the site per session. Longer sessions suggest more engaging content (GA4).' },
    ];
  });

  private readonly maxPageViews = computed(() =>
    Math.max(1, ...(this.data()?.topPages ?? []).map(p => p.views)));

  constructor(private readonly api: AnalyticsService) {}

  ngOnInit(): void {
    this.load();
    this.api.getHealth().subscribe({
      next: h => this.health.set(h),
      error: () => this.health.set({ ga4: false, searchConsole: false }),
    });
  }

  changePeriod(p: AnalyticsPeriod): void {
    this.period.set(p);
    this.load();
  }

  barWidth(views: number): number {
    return (views / this.maxPageViews()) * 100;
  }

  posClass(position: number): string {
    if (position <= 10) return 'good';
    if (position <= 30) return 'mid';
    return 'low';
  }

  private num(value: number): string {
    return value.toLocaleString('en-US');
  }

  private duration(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.round(seconds % 60);
    return m > 0 ? `${m}m ${s}s` : `${s}s`;
  }

  private load(): void {
    this.loading.set(true);
    this.api.getWebsite(this.period()).subscribe({
      next: d => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: () => {
        this.data.set(null);
        this.loading.set(false);
      },
    });
  }
}
