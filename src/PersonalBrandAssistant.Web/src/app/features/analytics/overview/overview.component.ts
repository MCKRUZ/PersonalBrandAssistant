import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChartModule } from 'primeng/chart';
import { AnalyticsService } from '../services/analytics.service';
import { AnalyticsPeriod } from '../models/analytics.model';
import { AnalyticsOverview, OverviewChannel } from '../models/channel-analytics.model';
import { PeriodSelectorComponent } from '../shared/period-selector.component';
import { ANALYTICS_CARD_STYLES } from '../shared/analytics-cards.styles';
import { CHART_PALETTE, periodDisplayLabel } from '../shared/analytics-shared';

interface ChannelView {
  readonly channel: OverviewChannel;
  readonly chartData: unknown;
}

@Component({
  selector: 'app-overview',
  standalone: true,
  imports: [CommonModule, ChartModule, PeriodSelectorComponent],
  template: `
    <div class="overview">
      <header class="page-head">
        <div class="page-head-text">
          <h1 class="page-title">Overview</h1>
          <p class="page-sub">All channels · last {{ periodLabel() }}</p>
        </div>
        <app-period-selector [period]="period()" (periodChange)="changePeriod($event)" />
      </header>

      @if (loading()) {
        <div class="total-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
        <div class="kpi-grid">
          @for (i of skeletonCells; track i) {
            <div class="kpi-card skeleton-card"><div class="sk sk-line"></div><div class="sk sk-value"></div></div>
          }
        </div>
      } @else {
        @if (data(); as d) {
        <section class="total-card">
          <span class="total-label">Total Audience <span class="approx-tag" title="Approximate — YouTube rounds subscriber counts">(approximate)</span></span>
          <span class="total-value">{{ d.totalAudience | number }}</span>
        </section>

        @if (d.combinedKpis.length) {
          <section class="kpi-grid">
            @for (k of d.combinedKpis; track k.key) {
              <div class="kpi-card">
                <span class="kpi-label">{{ k.label }}</span>
                <span class="kpi-value">{{ k.value | number }}</span>
                @if (k.deltaPct !== null) {
                  <span class="kpi-delta" [class.up]="k.deltaPct >= 0" [class.down]="k.deltaPct < 0">
                    {{ k.deltaPct >= 0 ? '▲' : '▼' }} {{ absPct(k.deltaPct) | number:'1.0-1' }}%
                  </span>
                }
              </div>
            }
          </section>
        }

        <section class="channels">
          @for (v of channelViews(); track v.channel.platform) {
            <div class="channel-card">
              <div class="channel-head">
                <span class="channel-name">{{ v.channel.platform }}</span>
                <span class="status-pill" [class]="statusClass(v.channel.status)">{{ statusLabel(v.channel.status) }}</span>
              </div>
              <div class="channel-followers">
                <span class="followers-value">{{ (v.channel.followers ?? 0) | number }}</span>
                <span class="followers-label">followers</span>
              </div>
              @if (v.channel.followerSparkline.length) {
                <div class="spark-wrap">
                  <p-chart type="line" [data]="v.chartData" [options]="sparkOptions" />
                </div>
              } @else {
                <div class="spark-empty">No trend yet</div>
              }
            </div>
          }
        </section>
        } @else {
          <div class="empty empty-page">
            <i class="pi pi-chart-bar"></i>
            <p>No overview data available</p>
          </div>
        }
      }
    </div>
  `,
  styles: [ANALYTICS_CARD_STYLES, `
    .overview { padding: 24px 28px; max-width: 1280px; margin: 0 auto; }

    .total-card {
      display: flex; flex-direction: column; gap: 6px;
      background: var(--surface-card); border: 1px solid var(--surface-border);
      border-radius: var(--r); padding: 20px 22px; margin-bottom: 18px;
    }
    .total-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
    .approx-tag { text-transform: none; letter-spacing: 0; color: var(--text-muted); font-style: italic; }
    .total-value { font-family: var(--font-mono); font-size: 36px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }

    .channels { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }
    .channel-card { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 16px 18px; }
    .channel-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 10px; }
    .channel-name { font-family: var(--font-display); font-size: 16px; color: var(--text-primary); }
    .status-pill { font-size: 10px; text-transform: uppercase; letter-spacing: .04em; padding: 2px 8px; border-radius: var(--r-pill); }
    .status-pill.connected { background: color-mix(in srgb, var(--status-approved) 16%, transparent); color: var(--status-approved); }
    .status-pill.reconnect { background: color-mix(in srgb, #fbbf24 16%, transparent); color: #fbbf24; }
    .status-pill.disconnected { background: var(--surface-hover); color: var(--text-secondary); }
    .channel-followers { display: flex; align-items: baseline; gap: 6px; margin-bottom: 12px; }
    .followers-value { font-family: var(--font-mono); font-size: 22px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
    .followers-label { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--text-secondary); }
    .spark-wrap { height: 60px; }
    .spark-empty { height: 60px; display: grid; place-items: center; color: var(--text-muted); font-size: 12px; }
    .empty-page { padding: 80px 16px; }
  `],
})
export class OverviewComponent implements OnInit {
  readonly skeletonCells = [0, 1, 2, 3];

  readonly period = signal<AnalyticsPeriod>('30d');
  readonly loading = signal(false);
  readonly data = signal<AnalyticsOverview | null>(null);

  readonly sparkOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false }, tooltip: { enabled: false } },
    elements: { point: { radius: 0 } },
    scales: { x: { display: false }, y: { display: false } },
  };

  readonly periodLabel = computed(() => periodDisplayLabel(this.period()));

  // Chart data precomputed once per data change (stable object refs — no per-CD reallocation).
  readonly channelViews = computed<ChannelView[]>(() =>
    (this.data()?.channels ?? []).map((channel, index) => ({
      channel,
      chartData: this.buildSparkData(channel, index),
    })));

  constructor(private readonly api: AnalyticsService) {}

  ngOnInit(): void {
    this.load();
  }

  changePeriod(p: AnalyticsPeriod): void {
    this.period.set(p);
    this.load();
  }

  absPct(delta: number): number {
    return Math.abs(delta);
  }

  statusClass(status: OverviewChannel['status']): string {
    return status === 'Connected' ? 'connected' : status === 'ReconnectRequired' ? 'reconnect' : 'disconnected';
  }

  statusLabel(status: OverviewChannel['status']): string {
    return status === 'Connected' ? 'Connected' : status === 'ReconnectRequired' ? 'Reconnect' : 'Not connected';
  }

  private buildSparkData(channel: OverviewChannel, index: number) {
    const color = CHART_PALETTE[index % CHART_PALETTE.length];
    return {
      labels: channel.followerSparkline.map(p => p.date),
      datasets: [{
        data: channel.followerSparkline.map(p => p.value),
        borderColor: color,
        backgroundColor: color,
        borderWidth: 2,
        tension: 0.35,
        fill: false,
      }],
    };
  }

  private load(): void {
    this.loading.set(true);
    this.api.getOverview(this.period()).subscribe({
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
