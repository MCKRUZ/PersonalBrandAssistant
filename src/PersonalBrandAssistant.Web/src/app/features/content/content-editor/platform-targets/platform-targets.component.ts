import { Component, computed, input, output } from '@angular/core';
import {
  Platform,
  PUBLISHABLE_PLATFORMS,
  PLATFORM_CHAR_LIMITS,
} from '../../models/content.model';
import type { PlatformConnectionStatus } from '../../models/content.model';
import { platformIconClass } from '../../content-list/content-display.utils';

@Component({
  selector: 'app-platform-targets',
  standalone: true,
  template: `
    <div class="platform-targets">
      @for (platform of platforms; track platform) {
        <label class="platform-checkbox"
               [attr.data-platform]="platform"
               [class.disabled]="!isConnected(platform) || isPrimary(platform)">
          <input type="checkbox"
            [checked]="isSelected(platform)"
            [disabled]="!isConnected(platform) || isPrimary(platform)"
            (change)="togglePlatform(platform)" />
          <i [class]="iconClass(platform)"></i>
          <span class="platform-label">{{ platform }}</span>
          @if (isSelected(platform) && charLimit(platform)) {
            <span class="char-count" [class.over-limit]="bodyLength() > charLimit(platform)!">
              {{ bodyLength() }}/{{ charLimit(platform) }}
            </span>
          }
          @if (isSelected(platform) && !charLimit(platform)) {
            <span class="word-count">{{ wordCount() }} words</span>
          }
        </label>
      }
    </div>
  `,
  styles: [`
    .platform-targets {
      display: flex; align-items: center; gap: 16px;
      padding: 6px 16px; border-bottom: 1px solid var(--surface-elevated); flex-shrink: 0;
      overflow-x: auto;
    }
    .platform-checkbox {
      display: flex; align-items: center; gap: 6px; font-size: 13px;
      color: var(--text-primary); cursor: pointer; white-space: nowrap;
    }
    .platform-checkbox.disabled { opacity: 0.5; cursor: not-allowed; }
    .platform-checkbox input { cursor: inherit; }
    .platform-label { font-size: 12px; }
    .char-count, .word-count { font-size: 11px; color: var(--text-secondary); }
    .char-count.over-limit { color: var(--voice-low); font-weight: 600; }
  `],
})
export class PlatformTargetsComponent {
  readonly selectedPlatforms = input.required<Platform[]>();
  readonly primaryPlatform = input.required<Platform>();
  readonly connectedPlatforms = input.required<PlatformConnectionStatus[]>();
  readonly bodyLength = input.required<number>();
  readonly wordCount = input.required<number>();
  readonly targetPlatformsChange = output<Platform[]>();

  readonly platforms = PUBLISHABLE_PLATFORMS;
  readonly iconClass = platformIconClass;

  private readonly connectedSet = computed(() =>
    new Set(this.connectedPlatforms().filter((c) => c.isConnected).map((c) => c.platform))
  );

  isConnected(platform: Platform): boolean {
    return this.connectedSet().has(platform);
  }

  isPrimary(platform: Platform): boolean {
    return platform === this.primaryPlatform();
  }

  isSelected(platform: Platform): boolean {
    return this.isPrimary(platform) || this.selectedPlatforms().includes(platform);
  }

  charLimit(platform: Platform): number | undefined {
    return PLATFORM_CHAR_LIMITS[platform];
  }

  togglePlatform(platform: Platform): void {
    const current = this.selectedPlatforms();
    const next = current.includes(platform)
      ? current.filter((p) => p !== platform)
      : [...current, platform];
    this.targetPlatformsChange.emit(next);
  }
}
