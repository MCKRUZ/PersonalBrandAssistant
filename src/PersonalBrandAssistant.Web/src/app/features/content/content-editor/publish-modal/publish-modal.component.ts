import { Component, effect, input, output, signal, computed } from '@angular/core';
import {
  Platform,
  PUBLISHABLE_PLATFORMS,
  PLATFORM_CHAR_LIMITS,
} from '../../models/content.model';
import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';
import { platformIconClass } from '../../content-list/content-display.utils';

@Component({
  selector: 'app-publish-modal',
  standalone: true,
  template: `
    @if (visible()) {
      <div class="modal-backdrop" (click)="onCancel()">
        <div class="modal-content" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
          <h3 class="modal-header">
            {{ mode() === 'schedule' ? 'Schedule to Platforms' : 'Publish to Platforms' }}
          </h3>

          <div class="platform-list">
            <div class="platform-row primary" data-testid="primary-platform">
              <label>
                <input type="checkbox" [checked]="true" [disabled]="true" />
                <i [class]="iconClass(content().primaryPlatform)"></i>
                <span class="platform-name">{{ content().primaryPlatform }}</span>
                <span class="primary-badge">Primary</span>
              </label>
              <span class="connection-status" [class]="connectionClass(content().primaryPlatform)">
                {{ connectionLabel(content().primaryPlatform) }}
              </span>
            </div>

            @for (platform of secondaryPlatforms(); track platform) {
              <div class="platform-row" [attr.data-platform]="platform">
                <label>
                  <input type="checkbox"
                    [checked]="isSelected(platform)"
                    [disabled]="!isConnected(platform)"
                    (change)="togglePlatform(platform)" />
                  <i [class]="iconClass(platform)"></i>
                  <span class="platform-name">{{ platform }}</span>
                  @if (charLimit(platform) && isSelected(platform)) {
                    <span class="char-info"
                          [class.over-limit]="(content().body?.length ?? 0) > charLimit(platform)!">
                      {{ content().body?.length ?? 0 }}/{{ charLimit(platform) }}
                    </span>
                  }
                </label>
                <span class="connection-status" [class]="connectionClass(platform)">
                  {{ connectionLabel(platform) }}
                </span>
              </div>
            }
          </div>

          @if (mode() === 'schedule') {
            <div class="schedule-picker">
              <label>
                Schedule for:
                <input type="datetime-local" [value]="scheduledAt()"
                       (input)="scheduledAt.set(asInputValue($event))"
                       data-testid="schedule-input" />
              </label>
            </div>
          }

          <div class="modal-actions">
            <button class="btn-cancel" data-testid="cancel-btn" (click)="onCancel()">Cancel</button>
            <button class="btn-confirm" data-testid="confirm-btn"
                    [disabled]="selectedPlatforms().length === 0 || (mode() === 'schedule' && !scheduledAt())"
                    (click)="onConfirm()">
              {{ mode() === 'schedule' ? 'Schedule' : 'Publish' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .modal-backdrop {
      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.6);
      display: flex; align-items: center; justify-content: center; z-index: 1000;
    }
    .modal-content {
      background: #161b22; border: 1px solid #30363d; border-radius: 12px;
      padding: 24px; width: 480px; max-width: 90vw; max-height: 80vh; overflow-y: auto;
    }
    .modal-header {
      margin: 0 0 16px; font-size: 18px; color: #f0f6fc; font-weight: 600;
    }
    .platform-list { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
    .platform-row {
      display: flex; align-items: center; justify-content: space-between;
      padding: 8px 12px; border-radius: 8px; background: #0d1117;
    }
    .platform-row.primary { border: 1px solid #c8715640; }
    .platform-row label {
      display: flex; align-items: center; gap: 8px; cursor: pointer; color: #c9d1d9; font-size: 14px;
    }
    .platform-name { font-weight: 500; }
    .primary-badge {
      font-size: 11px; background: #c8715633; color: #c87156;
      padding: 2px 8px; border-radius: 10px;
    }
    .connection-status { font-size: 12px; }
    .connection-status.connected { color: #3fb950; }
    .connection-status.disconnected { color: #f85149; }
    .connection-status.expiring { color: #d29922; }
    .char-info { font-size: 11px; color: #8b949e; }
    .char-info.over-limit { color: #f85149; font-weight: 600; }
    .schedule-picker { margin-bottom: 16px; }
    .schedule-picker label { color: #c9d1d9; font-size: 14px; display: flex; align-items: center; gap: 8px; }
    .schedule-picker input {
      background: #0d1117; border: 1px solid #30363d; border-radius: 6px;
      padding: 6px 10px; color: #f0f6fc; font-family: inherit;
    }
    .modal-actions { display: flex; justify-content: flex-end; gap: 8px; }
    .btn-cancel {
      background: transparent; border: 1px solid #30363d; color: #c9d1d9;
      border-radius: 6px; padding: 8px 16px; cursor: pointer; font-family: inherit; font-size: 14px;
    }
    .btn-confirm {
      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
      padding: 8px 20px; cursor: pointer; font-family: inherit; font-size: 14px; font-weight: 500;
    }
    .btn-confirm:disabled { opacity: 0.5; cursor: not-allowed; }
    .btn-confirm:hover:not(:disabled) { background: #d4836a; }
  `],
})
export class PublishModalComponent {
  readonly visible = input.required<boolean>();
  readonly content = input.required<ContentDetail>();
  readonly connectedPlatforms = input.required<PlatformConnectionStatus[]>();
  readonly mode = input<'publish' | 'schedule'>('publish');

  readonly confirm = output<{ platforms: Platform[]; scheduledAt?: string }>();
  readonly cancel = output<void>();

  readonly scheduledAt = signal('');
  readonly iconClass = platformIconClass;

  private readonly selected = signal<Platform[]>([]);

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.selected.set([]);
        this.scheduledAt.set('');
      }
    });
  }

  readonly secondaryPlatforms = computed(() =>
    PUBLISHABLE_PLATFORMS.filter((p) => p !== this.content().primaryPlatform)
  );

  readonly selectedPlatforms = computed(() => {
    const primary = this.content().primaryPlatform;
    const secondary = this.selected().length > 0
      ? this.selected()
      : (this.content().targetPlatforms ?? []).filter((p) => p !== primary);
    return [primary, ...secondary];
  });

  isSelected(platform: Platform): boolean {
    return this.selectedPlatforms().includes(platform);
  }

  isConnected(platform: Platform): boolean {
    return this.connectedPlatforms().some((c) => c.platform === platform && c.isConnected);
  }

  connectionClass(platform: Platform): string {
    const conn = this.connectedPlatforms().find((c) => c.platform === platform);
    if (!conn || !conn.isConnected) return 'disconnected';
    if (conn.isExpiring) return 'expiring';
    return 'connected';
  }

  connectionLabel(platform: Platform): string {
    const conn = this.connectedPlatforms().find((c) => c.platform === platform);
    if (!conn || !conn.isConnected) return 'Not connected';
    if (conn.isExpiring) return 'Expiring';
    return 'Connected';
  }

  charLimit(platform: Platform): number | undefined {
    return PLATFORM_CHAR_LIMITS[platform];
  }

  togglePlatform(platform: Platform): void {
    const current = this.selected().length > 0
      ? this.selected()
      : (this.content().targetPlatforms ?? []).filter((p) => p !== this.content().primaryPlatform);
    const next = current.includes(platform)
      ? current.filter((p) => p !== platform)
      : [...current, platform];
    this.selected.set(next);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onConfirm(): void {
    const result: { platforms: Platform[]; scheduledAt?: string } = {
      platforms: this.selectedPlatforms(),
    };
    if (this.mode() === 'schedule' && this.scheduledAt()) {
      result.scheduledAt = new Date(this.scheduledAt()).toISOString();
    }
    this.confirm.emit(result);
  }

  onCancel(): void {
    this.cancel.emit();
  }
}
