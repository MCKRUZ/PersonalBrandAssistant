import { Component, OnInit, computed, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChartModule } from 'primeng/chart';
import { AnalyticsService } from '../services/analytics.service';
import { AnalyticsPeriod } from '../models/analytics.model';
import {
  ChannelAnalytics,
  ChannelPlatform,
  YouTubeDeepAnalytics,
  YouTubeMetricSeries,
} from '../models/channel-analytics.model';
import { PeriodSelectorComponent } from '../shared/period-selector.component';
import { ANALYTICS_CARD_STYLES } from '../shared/analytics-cards.styles';
import { CHART_PALETTE, periodDisplayLabel } from '../shared/analytics-shared';

interface LineChart {
  readonly title: string;
  readonly chartData: unknown;
}

interface BreakdownRow {
  readonly label: string;
  readonly value: number;
}

const PLATFORM_TITLES: Record<ChannelPlatform, string> = { youtube: 'YouTube', instagram: 'Instagram', tiktok: 'TikTok' };

@Component({
  selector: 'app-channel-analytics',
  standalone: true,
  imports: [CommonModule, ChartModule, PeriodSelectorComponent],
  template: `
    <div class="channel">
      <header class="page-head">
        <div class="page-head-text">
          <h1 class="page-title">{{ title() }}</h1>
          <p class="page-sub">Channel analytics · last {{ periodLabel() }}</p>
        </div>
        <app-period-selector [period]="period()" (periodChange)="changePeriod($event)" />
      </header>

      @if (loading()) {
        <div class="kpi-grid">
          @for (i of skeletonCells; track i) {
            <div class="kpi-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
          }
        </div>
      } @else if (status() === 'NotConnected') {
        <div class="connect-cta">
          <i class="pi pi-link"></i>
          <p>{{ title() }} is not connected.</p>
          <a class="connect-btn" [href]="authUrl()">Connect {{ title() }}</a>
        </div>
      } @else if (status() === 'ReconnectRequired') {
        <div class="connect-cta">
          <i class="pi pi-exclamation-triangle"></i>
          <p>Your {{ title() }} connection expired.</p>
          <a class="connect-btn" [href]="authUrl()">Reconnect</a>
        </div>
      } @else {
        @if (data(); as d) {
          @if (d.kpis.length) {
            <section class="kpi-grid">
              @for (k of d.kpis; track k.key) {
                <div class="kpi-card">
                  <span class="kpi-label">{{ k.label }}</span>
                  <span class="kpi-value">{{ k.value | number }}</span>
                  <div class="kpi-meta">
                    @if (k.deltaPct !== null) {
                      <span class="kpi-delta" [class.up]="k.deltaPct >= 0" [class.down]="k.deltaPct < 0">
                        {{ k.deltaPct >= 0 ? '▲' : '▼' }} {{ absPct(k.deltaPct) | number:'1.0-1' }}%
                      </span>
                    }
                    @if (k.rate !== null) {
                      <span class="kpi-rate">{{ (k.rate * 100) | number:'1.0-2' }}% eng.</span>
                    }
                  </div>
                </div>
              }
            </section>
          }

          @if (trendCharts().length) {
            <section class="panels">
              @for (c of trendCharts(); track c.title) {
                <div class="panel">
                  <div class="panel-head"><h2>{{ c.title }}</h2></div>
                  <div class="trend-wrap">
                    <p-chart type="line" [data]="c.chartData" [options]="trendOptions" />
                  </div>
                </div>
              }
            </section>
          }

          @if (d.recentPosts.length) {
            <section class="panel">
              <div class="panel-head"><h2>Recent Posts</h2></div>
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Title</th>
                    @for (col of postColumns(); track col) { <th class="num">{{ col }}</th> }
                  </tr>
                </thead>
                <tbody>
                  @for (row of d.recentPosts; track row.videoId) {
                    <tr>
                      <td class="title" [title]="row.title ?? row.videoId">{{ row.title ?? row.videoId }}</td>
                      @for (col of postColumns(); track col) {
                        <td class="num">{{ (row.metrics[col] ?? 0) | number }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }

          @if (showDeep()) {
            <section class="deep">
              <div class="panel-head"><h2>YouTube Deep Analytics</h2></div>
              @if (deepDayCharts().length) {
                <div class="panels">
                  @for (c of deepDayCharts(); track c.title) {
                    <div class="panel">
                      <div class="panel-head"><h2>{{ c.title }}</h2></div>
                      <div class="trend-wrap">
                        <p-chart type="line" [data]="c.chartData" [options]="trendOptions" />
                      </div>
                    </div>
                  }
                </div>
              }
              @for (bd of deepBreakdowns(); track bd.title) {
                @if (bd.rows.length) {
                  <div class="panel">
                    <div class="panel-head"><h2>{{ bd.title }}</h2></div>
                    <ul class="breakdown">
                      @for (b of bd.rows; track b.label) {
                        <li><span class="bk-label">{{ b.label }}</span><span class="bk-value">{{ b.value | number }}</span></li>
                      }
                    </ul>
                  </div>
                }
              }
            </section>
          } @else if (platform() === 'youtube' && deepFailed()) {
            <p class="deep-unavailable">Deep analytics unavailable right now.</p>
          }
        } @else if (loadError()) {
          <div class="empty empty-page">
            <i class="pi pi-exclamation-circle"></i>
            <p>Couldn't load {{ title() }} analytics.</p>
            <button type="button" class="retry-btn" (click)="reload()">Retry</button>
          </div>
        } @else {
          <div class="empty empty-page">
            <i class="pi pi-chart-bar"></i>
            <p>No analytics data available</p>
          </div>
        }
      }
    </div>
  `,
  styles: [ANALYTICS_CARD_STYLES, `
    .channel { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }

    .connect-cta {
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px;
      background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r);
      padding: 56px 24px; text-align: center; color: var(--text-secondary);
    }
    .connect-cta i { font-size: 30px; color: var(--brand-primary); }
    .connect-cta p { margin: 0; font-size: 14px; }
    .connect-btn {
      display: inline-block; margin-top: 4px; padding: 10px 20px; border-radius: var(--r-control);
      background: var(--brand-primary); color: #fff; font-size: 14px; text-decoration: none; font-weight: 500;
    }
    .connect-btn:hover { filter: brightness(1.08); }

    .kpi-meta { display: flex; gap: 10px; align-items: center; }
    .kpi-rate { font-size: 11px; color: var(--text-secondary); }

    .panels { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 14px; margin-bottom: 14px; }
    .trend-wrap { height: 200px; }
    .title { max-width: 340px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .deep { margin-top: 8px; }
    .breakdown { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .breakdown li { display: flex; justify-content: space-between; gap: 10px; font-size: 13px; }
    .bk-label { color: var(--text-primary); }
    .bk-value { font-family: var(--font-mono); color: var(--text-secondary); font-variant-numeric: tabular-nums; }
    .deep-unavailable { color: var(--text-muted); font-size: 13px; font-style: italic; padding: 8px 2px; }

    .empty-page { padding: 80px 16px; }
    .retry-btn {
      margin-top: 4px; padding: 8px 18px; border-radius: var(--r-control);
      background: var(--surface-hover); color: var(--text-primary); border: 1px solid var(--surface-border);
      font-size: 13px; cursor: pointer;
    }
    .retry-btn:hover { border-color: var(--surface-disabled); }
  `],
})
export class ChannelAnalyticsComponent implements OnInit {
  readonly platform = input.required<ChannelPlatform>();

  readonly skeletonCells = [0, 1, 2, 3];

  readonly period = signal<AnalyticsPeriod>('30d');
  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly data = signal<ChannelAnalytics | null>(null);
  readonly deep = signal<YouTubeDeepAnalytics | null>(null);
  readonly deepFailed = signal(false);

  readonly status = computed(() => this.data()?.status ?? null);
  readonly title = computed(() => PLATFORM_TITLES[this.platform()]);
  readonly periodLabel = computed(() => periodDisplayLabel(this.period()));
  readonly showDeep = computed(() => this.platform() === 'youtube' && this.deep() !== null && !this.deepFailed());

  readonly trendOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    elements: { point: { radius: 0 } },
    scales: {
      x: { ticks: { color: '#8a8a96' }, grid: { display: false } },
      y: { ticks: { color: '#8a8a96' }, grid: { color: 'rgba(255,255,255,0.05)' } },
    },
  };

  // Union of metric keys across recent posts, so the table shows every metric present.
  readonly postColumns = computed<string[]>(() => {
    const keys = new Set<string>();
    for (const p of this.data()?.recentPosts ?? []) {
      for (const k of Object.keys(p.metrics)) keys.add(k);
    }
    return [...keys];
  });

  // Chart data precomputed per data/deep change (stable refs — no per-CD reallocation).
  readonly trendCharts = computed<LineChart[]>(() =>
    (this.data()?.trends ?? []).map((t, i) => ({
      title: t.metric,
      chartData: this.buildLineData(t.points.map(p => ({ label: p.date, value: p.value })), i),
    })));

  readonly deepDayCharts = computed<LineChart[]>(() =>
    (this.deep()?.daySeries ?? []).map((s, i) => ({
      title: s.metric,
      chartData: this.buildLineData(s.points.map(p => ({ label: p.day, value: p.value })), i),
    })));

  // Aggregate each labeled series across the whole range (sum of daily points), not last-day sampling.
  readonly deepBreakdowns = computed<{ title: string; rows: BreakdownRow[] }[]>(() => {
    const d = this.deep();
    if (!d) return [];
    return [
      { title: 'Traffic Sources', rows: this.sumBreakdown(d.trafficSources) },
      { title: 'Geography', rows: this.sumBreakdown(d.geography) },
      { title: 'Demographics', rows: this.sumBreakdown(d.demographics) },
    ];
  });

  constructor(private readonly api: AnalyticsService) {}

  ngOnInit(): void {
    this.load();
  }

  changePeriod(p: AnalyticsPeriod): void {
    this.period.set(p);
    this.load();
  }

  reload(): void {
    this.load();
  }

  absPct(delta: number): number {
    return Math.abs(delta);
  }

  authUrl(): string {
    return `/api/auth/${this.platform()}/authorize?purpose=analytics`;
  }

  private buildLineData(points: ReadonlyArray<{ label: string; value: number }>, index: number) {
    const color = CHART_PALETTE[index % CHART_PALETTE.length];
    return {
      labels: points.map(p => p.label),
      datasets: [{ data: points.map(p => p.value), borderColor: color, backgroundColor: color, borderWidth: 2, tension: 0.3, fill: false }],
    };
  }

  private sumBreakdown(series: ReadonlyArray<YouTubeMetricSeries>): BreakdownRow[] {
    return series.map(s => ({ label: s.metric, value: s.points.reduce((sum, p) => sum + p.value, 0) }));
  }

  private load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.deep.set(null);
    this.deepFailed.set(false);
    const platform = this.platform();
    this.api.getChannel(platform, this.period()).subscribe({
      next: d => {
        this.data.set(d);
        this.loading.set(false);
        if (platform === 'youtube' && d.status === 'Connected') {
          this.loadDeep();
        }
      },
      error: () => {
        this.data.set(null);
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadDeep(): void {
    this.deepFailed.set(false);
    this.api.getYouTubeDeep(this.period()).subscribe({
      next: dd => this.deep.set(dd),
      error: () => {
        this.deep.set(null);
        this.deepFailed.set(true);
      },
    });
  }
}
