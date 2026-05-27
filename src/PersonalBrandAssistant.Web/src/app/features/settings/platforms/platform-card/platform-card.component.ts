import { Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import type {
  PlatformConfig,
  PlatformStatus,
  StoreCredentialsRequest,
} from '../../models/platform-connection.model';
import { MediumTokenFormComponent } from '../medium-token-form/medium-token-form.component';
import { SubstackLoginFormComponent } from '../substack-login-form/substack-login-form.component';

@Component({
  selector: 'app-platform-card',
  standalone: true,
  imports: [DatePipe, MediumTokenFormComponent, SubstackLoginFormComponent],
  template: `
    <div class="card">
      <div class="card-header">
        <div class="platform-info">
          <span class="platform-name">{{ config().displayName }}</span>
          <span class="platform-desc">{{ config().description }}</span>
        </div>
        <div class="status-badge" [class]="statusClass()">
          <span class="dot"></span>
          {{ statusText() }}
        </div>
      </div>

      <div class="card-details">
        @if (status()?.expiresAt && isConnected()) {
          <span class="detail">Expires: {{ status()!.expiresAt | date:'mediumDate' }}</span>
        }
        @if (status()?.lastPublishDate) {
          <span class="detail">Last published: {{ status()!.lastPublishDate | date:'mediumDate' }}</span>
        }
      </div>

      <div class="card-actions">
        @if (config().connectionType === 'none') {
          <!-- Blog: always connected, no actions -->
        } @else if (isConnected()) {
          <button class="btn-disconnect" (click)="disconnect.emit(config().platform)" [disabled]="loading()">
            Disconnect
          </button>
        } @else {
          @if (!showForm) {
            <button class="btn-connect" (click)="onConnectClick()" [disabled]="loading()">
              {{ status()?.status === 'Expired' ? 'Reconnect' : 'Connect' }}
            </button>
          }
        }
      </div>

      @if (showForm && config().connectionType === 'token') {
        <app-medium-token-form
          [loading]="loading()"
          (submitted)="onCredentialsSubmitted($event)"
        />
      }
      @if (showForm && config().connectionType === 'login') {
        <app-substack-login-form
          [loading]="loading()"
          [errorMessage]="credentialError()"
          (submitted)="onCredentialsSubmitted($event)"
        />
      }
    </div>
  `,
  styles: [`
    .card {
      background: #141418; border: 1px solid #2c2c36; border-radius: 8px;
      padding: 20px; display: flex; flex-direction: column; gap: 12px;
    }
    .card-header { display: flex; justify-content: space-between; align-items: flex-start; }
    .platform-info { display: flex; flex-direction: column; gap: 2px; }
    .platform-name { font-size: 16px; font-weight: 600; color: #f0f0f5; }
    .platform-desc { font-size: 13px; color: #8a8a96; }
    .status-badge {
      display: flex; align-items: center; gap: 6px;
      font-size: 13px; padding: 4px 10px; border-radius: 12px;
    }
    .dot { width: 8px; height: 8px; border-radius: 50%; }
    .status-connected { color: #4ade80; background: rgba(74, 222, 128, 0.1); }
    .status-connected .dot { background: #4ade80; }
    .status-expired { color: #fbbf24; background: rgba(251, 191, 36, 0.1); }
    .status-expired .dot { background: #fbbf24; }
    .status-not-configured { color: #8a8a96; background: rgba(138, 138, 150, 0.1); }
    .status-not-configured .dot { background: #5a5a66; }
    .card-details { display: flex; flex-direction: column; gap: 4px; }
    .detail { font-size: 13px; color: #8a8a96; }
    .card-actions { display: flex; gap: 8px; }
    .btn-connect {
      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
      padding: 8px 16px; font-size: 14px; cursor: pointer; font-family: inherit;
    }
    .btn-connect:hover:not(:disabled) { background: #d4836a; }
    .btn-connect:disabled { opacity: 0.5; cursor: not-allowed; }
    .btn-disconnect {
      background: transparent; color: #f87171; border: 1px solid #f87171; border-radius: 6px;
      padding: 8px 16px; font-size: 14px; cursor: pointer; font-family: inherit;
    }
    .btn-disconnect:hover:not(:disabled) { background: rgba(248, 113, 113, 0.1); }
    .btn-disconnect:disabled { opacity: 0.5; cursor: not-allowed; }
  `],
})
export class PlatformCardComponent {
  readonly config = input.required<PlatformConfig>();
  readonly status = input<PlatformStatus | null>(null);
  readonly loading = input(false);
  readonly credentialError = input<string | null>(null);
  readonly connect = output<string>();
  readonly disconnect = output<string>();
  readonly credentialsSubmitted = output<{
    platform: string;
    credentials: StoreCredentialsRequest;
  }>();

  showForm = false;

  readonly isConnected = computed(() =>
    this.config().connectionType === 'none' || this.status()?.status === 'Connected'
  );

  readonly statusClass = computed(() => {
    if (this.config().connectionType === 'none') return 'status-connected';
    switch (this.status()?.status) {
      case 'Connected': return 'status-connected';
      case 'Expired': return 'status-expired';
      default: return 'status-not-configured';
    }
  });

  readonly statusText = computed(() => {
    if (this.config().connectionType === 'none') return 'Always Connected';
    switch (this.status()?.status) {
      case 'Connected': return 'Connected';
      case 'Expired': return 'Expired';
      default: return 'Not Connected';
    }
  });

  onConnectClick(): void {
    if (this.config().connectionType === 'oauth') {
      this.connect.emit(this.config().platform);
    } else {
      this.showForm = !this.showForm;
    }
  }

  onCredentialsSubmitted(credentials: StoreCredentialsRequest): void {
    this.credentialsSubmitted.emit({
      platform: this.config().platform,
      credentials,
    });
  }
}
