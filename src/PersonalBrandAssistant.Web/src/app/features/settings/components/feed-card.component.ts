import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { Tooltip } from 'primeng/tooltip';
import { NewsSource, getFeedHealth, FEED_HEALTH_COLORS, FeedHealthStatus } from '../../news/models/news.model';

@Component({
  selector: 'app-feed-card',
  standalone: true,
  imports: [FormsModule, ToggleSwitch, ButtonModule, Tag, Tooltip],
  template: `
    <div class="feed-card">
      <div class="feed-card__info">
        <div class="feed-card__name">
          <span
            class="feed-card__health-dot"
            [style.background]="healthColor()"
            [pTooltip]="healthTooltip()"
            tooltipPosition="top"
          ></span>
          {{ feed().name }}
        </div>
        @if (feed().feedUrl) {
          <div class="feed-card__url">{{ feed().feedUrl }}</div>
        }
        <div class="feed-card__meta">
          @if (feed().category) {
            <p-tag [value]="feed().category!" [style]="{ fontSize: '0.7rem' }" />
          }
          @if (feed().lastSuccessAt) {
            <span class="feed-card__sync">Last sync: {{ relativeTime() }}</span>
          }
        </div>
      </div>
      <div class="feed-card__actions">
        <p-toggleswitch
          [ngModel]="feed().isEnabled"
          (ngModelChange)="toggle.emit(feed().id)"
        />
        <button
          pButton
          icon="pi pi-trash"
          severity="danger"
          [outlined]="true"
          size="small"
          [rounded]="true"
          (click)="delete.emit(feed().id)"
        ></button>
      </div>
    </div>
  `,
  styles: [`
    .feed-card {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      padding: 0.75rem 1rem;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.06);
    }
    .feed-card__info {
      flex: 1;
      min-width: 0;
    }
    .feed-card__name {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-weight: 600;
      font-size: 0.88rem;
      color: #f4f4f5;
    }
    .feed-card__health-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .feed-card__url {
      font-size: 0.75rem;
      color: #71717a;
      margin: 0.15rem 0 0.4rem;
      padding-left: 1.05rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .feed-card__meta {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding-left: 1.05rem;
    }
    .feed-card__sync {
      font-size: 0.7rem;
      color: #52525b;
    }
    .feed-card__actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-shrink: 0;
    }
  `],
})
export class FeedCardComponent {
  feed = input.required<NewsSource>();
  toggle = output<string>();
  delete = output<string>();

  readonly health = computed<FeedHealthStatus>(() => getFeedHealth(this.feed()));
  readonly healthColor = computed(() => FEED_HEALTH_COLORS[this.health()]);

  readonly healthTooltip = computed(() => {
    const f = this.feed();
    const status = this.health();
    if (status === 'unknown') return 'Never polled';
    if (status === 'error') return `Failing (${f.consecutiveFailures} failures): ${f.lastError ?? 'Unknown error'}`;
    if (status === 'warning') return `Warning: ${f.lastError ?? 'Recent failure'}`;
    return 'Healthy';
  });

  readonly relativeTime = computed(() => {
    const ts = this.feed().lastSuccessAt;
    if (!ts) return '';
    const diff = Date.now() - new Date(ts).getTime();
    const minutes = Math.floor(diff / 60_000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
  });
}
