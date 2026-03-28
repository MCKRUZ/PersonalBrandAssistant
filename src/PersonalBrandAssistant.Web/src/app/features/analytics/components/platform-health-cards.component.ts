import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Tag } from 'primeng/tag';
import { PlatformSummary } from '../models/dashboard.model';
import { PLATFORM_COLORS, PLATFORM_ICONS, PLATFORM_LABELS } from '../../../shared/utils/platform-icons';

interface PlatformCardView {
  readonly platform: string;
  readonly color: string;
  readonly icon: string;
  readonly label: string;
  readonly followerLabel: string;
  readonly engagementLabel: string;
  readonly followerCount: number | null;
  readonly postCount: number;
  readonly avgEngagement: number;
  readonly topPostTitle: string | null;
  readonly isAvailable: boolean;
  readonly unavailableMessage: string;
}

function toFollowerLabel(platform: string): string {
  if (platform === 'YouTube') return 'Subscribers';
  if (platform === 'Reddit') return 'Karma';
  return 'Followers';
}

function toEngagementLabel(platform: string): string {
  if (platform === 'YouTube') return 'Avg Views';
  if (platform === 'Reddit') return 'Avg Score';
  return 'Avg Eng.';
}

@Component({
  selector: 'app-platform-health-cards',
  standalone: true,
  imports: [CommonModule, Tag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="platform-grid" role="group" aria-label="Platform health cards">
      @for (card of cards(); track card.platform) {
        <div class="platform-card" [style.--platform-color]="card.color" role="region" [attr.aria-label]="card.label + ' platform summary'">
          <div class="platform-header">
            <i [class]="card.icon" class="platform-icon" aria-hidden="true"></i>
            <span class="platform-name">{{ card.label }}</span>
          </div>

          @if (!card.isAvailable) {
            <div class="unavailable-overlay">
              <p-tag [value]="card.unavailableMessage" severity="warn" />
            </div>
          } @else {
            <div class="stat-rows">
              <div class="stat-row">
                <span class="stat-label">{{ card.followerLabel }}</span>
                <span class="stat-value">{{ card.followerCount !== null ? (card.followerCount | number) : 'N/A' }}</span>
              </div>
              <div class="stat-row">
                <span class="stat-label">Posts</span>
                <span class="stat-value">{{ card.postCount | number }}</span>
              </div>
              <div class="stat-row">
                <span class="stat-label">{{ card.engagementLabel }}</span>
                <span class="stat-value">{{ card.avgEngagement | number }}</span>
              </div>
            </div>

            @if (card.topPostTitle) {
              <div class="top-post">
                <span class="top-post-label">Top Post</span>
                <span class="top-post-title">{{ card.topPostTitle }}</span>
              </div>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .platform-grid {
      display: grid;
      grid-template-columns: repeat(5, 1fr);
      gap: 1rem;
    }
    @media (max-width: 1200px) {
      .platform-grid { grid-template-columns: repeat(3, 1fr); }
    }
    @media (max-width: 900px) {
      .platform-grid { grid-template-columns: repeat(2, 1fr); }
    }
    @media (max-width: 600px) {
      .platform-grid { grid-template-columns: 1fr; }
    }

    .platform-card {
      position: relative;
      overflow: hidden;
      background: var(--p-surface-900, #111118);
      border: 1px solid var(--p-surface-700, #25252f);
      border-radius: 12px;
      padding: 1rem 1.1rem;
      transition: border-color 0.2s ease, transform 0.2s ease;
    }
    .platform-card::before {
      content: '';
      position: absolute;
      top: 0;
      left: 0;
      right: 0;
      height: 3px;
      background: var(--platform-color);
    }
    .platform-card:hover {
      border-color: var(--p-surface-600, #3a3a48);
      transform: translateY(-1px);
    }

    .platform-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.75rem;
    }
    .platform-icon {
      font-size: 1rem;
      color: var(--platform-color);
    }
    .platform-name {
      font-size: 0.8rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--p-text-muted-color, #71717a);
    }

    .stat-rows {
      display: flex;
      flex-direction: column;
      gap: 0.4rem;
    }
    .stat-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .stat-label {
      font-size: 0.75rem;
      color: var(--p-text-muted-color, #71717a);
    }
    .stat-value {
      font-size: 0.85rem;
      font-weight: 700;
    }

    .top-post {
      margin-top: 0.6rem;
      padding-top: 0.6rem;
      border-top: 1px solid var(--p-surface-700, #25252f);
    }
    .top-post-label {
      display: block;
      font-size: 0.65rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--p-text-muted-color, #71717a);
      margin-bottom: 0.25rem;
    }
    .top-post-title {
      font-size: 0.78rem;
      font-weight: 500;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .unavailable-overlay {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 80px;
      opacity: 0.7;
    }
  `,
})
export class PlatformHealthCardsComponent {
  readonly platforms = input<readonly PlatformSummary[]>([]);

  readonly cards = computed<readonly PlatformCardView[]>(() =>
    this.platforms().map(p => ({
      platform: p.platform,
      color: PLATFORM_COLORS[p.platform as keyof typeof PLATFORM_COLORS] ?? '#888',
      icon: PLATFORM_ICONS[p.platform as keyof typeof PLATFORM_ICONS] ?? 'pi pi-circle',
      label: PLATFORM_LABELS[p.platform as keyof typeof PLATFORM_LABELS] ?? p.platform,
      followerLabel: toFollowerLabel(p.platform),
      engagementLabel: toEngagementLabel(p.platform),
      followerCount: p.followerCount,
      postCount: p.postCount,
      avgEngagement: p.avgEngagement,
      topPostTitle: p.topPostTitle,
      isAvailable: p.isAvailable,
      unavailableMessage: p.platform === 'LinkedIn' ? 'Coming Soon' : 'Data unavailable',
    }))
  );
}
