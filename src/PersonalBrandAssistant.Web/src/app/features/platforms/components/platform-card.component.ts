import { Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { Chip } from 'primeng/chip';
import { Platform } from '../../../shared/models';
import { PLATFORM_ICONS, PLATFORM_LABELS, PLATFORM_COLORS } from '../../../shared/utils/platform-icons';

@Component({
  selector: 'app-platform-card',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule, Chip],
  template: `
    <p-card styleClass="platform-card h-full">
      <div class="flex flex-column gap-3">
        <div class="flex align-items-center gap-3">
          <div class="platform-icon" [style.background-color]="color()">
            <i [class]="icon()" style="font-size: 1.5rem; color: white"></i>
          </div>
          <div class="flex-1">
            <h3 class="m-0">{{ label() }}</h3>
            <div class="flex align-items-center gap-2 mt-1">
              <span class="status-dot" [class.connected]="platform().isConnected"></span>
              <span class="text-sm" [class.text-green-400]="platform().isConnected" [class.text-red-400]="!platform().isConnected">
                {{ platform().isConnected ? 'Connected' : 'Disconnected' }}
              </span>
            </div>
          </div>
        </div>

        @if (platform().isConnected) {
          @if (platform().lastSyncAt) {
            <div class="text-sm text-color-secondary">
              <i class="pi pi-sync mr-1"></i>Last sync: {{ relativeTime() }}
            </div>
          }
          @if (tokenWarning()) {
            <div class="text-sm text-orange-400">
              <i class="pi pi-exclamation-triangle mr-1"></i>Token expires: {{ platform().tokenExpiresAt | date:'short' }}
            </div>
          }
          @if (platform().grantedScopes && platform().grantedScopes!.length > 0) {
            <div class="flex flex-wrap gap-1">
              @for (scope of platform().grantedScopes; track scope) {
                <p-chip [label]="scope" styleClass="scope-chip" />
              }
            </div>
          }
        }

        <div class="flex gap-2 mt-auto">
          @if (platform().isConnected) {
            <p-button label="Test Post" severity="secondary" size="small" [text]="true" icon="pi pi-send" (onClick)="testPost.emit()" />
            <p-button label="Disconnect" severity="danger" size="small" [text]="true" icon="pi pi-times" (onClick)="disconnect.emit()" />
          } @else {
            <p-button label="Connect" icon="pi pi-link" (onClick)="connect.emit()" />
          }
        </div>
      </div>
    </p-card>
  `,
  styles: `
    .platform-icon {
      width: 3rem;
      height: 3rem;
      border-radius: 0.5rem;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background-color: var(--red-400);
    }
    .status-dot.connected {
      background-color: var(--green-400);
    }
    :host ::ng-deep .scope-chip .p-chip {
      font-family: 'JetBrains Mono', monospace;
      font-size: 0.7rem;
      padding: 0.15rem 0.5rem;
      background: var(--surface-ground);
      border: 1px solid var(--surface-border);
    }
  `,
})
export class PlatformCardComponent {
  readonly platform = input.required<Platform>();
  readonly connect = output<void>();
  readonly disconnect = output<void>();
  readonly details = output<void>();
  readonly testPost = output<void>();

  readonly icon = computed(() => PLATFORM_ICONS[this.platform().type]);
  readonly label = computed(() => PLATFORM_LABELS[this.platform().type]);
  readonly color = computed(() => PLATFORM_COLORS[this.platform().type]);

  readonly relativeTime = computed(() => {
    const syncAt = this.platform().lastSyncAt;
    if (!syncAt) return '';
    const diff = Date.now() - new Date(syncAt).getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
  });

  readonly tokenWarning = computed(() => {
    const expires = this.platform().tokenExpiresAt;
    if (!expires) return false;
    const diff = new Date(expires).getTime() - Date.now();
    return diff > 0 && diff < 7 * 24 * 60 * 60 * 1000;
  });
}
