import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { PlatformConnectionService } from '../services/platform-connection.service';
import { PlatformCardComponent } from './platform-card/platform-card.component';
import type {
  PlatformConfig,
  PlatformStatus,
  StoreCredentialsRequest,
} from '../models/platform-connection.model';

@Component({
  selector: 'app-platform-connections',
  standalone: true,
  imports: [PlatformCardComponent],
  template: `
    @if (notification) {
      <div class="notification" [class]="notification.type">
        {{ notification.message }}
        <button class="dismiss" (click)="notification = null">&times;</button>
      </div>
    }

    @if (loading) {
      <div class="loading">Loading platforms...</div>
    } @else if (error) {
      <div class="error-state">
        <p>Failed to load platforms.</p>
        <button class="btn-retry" (click)="loadPlatforms()">Retry</button>
      </div>
    } @else {
      <div class="grid">
        @for (config of platformConfigs; track config.platform) {
          <app-platform-card
            [config]="config"
            [status]="getStatus(config.platform)"
            [loading]="loadingPlatform === config.platform"
            [credentialError]="credentialErrors[config.platform] ?? null"
            (connect)="onConnect($event)"
            (disconnect)="onDisconnect($event)"
            (credentialsSubmitted)="onCredentialsSubmitted($event)"
          />
        }
      </div>
    }
  `,
  styles: [`
    .grid {
      display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px;
    }
    @media (max-width: 768px) {
      .grid { grid-template-columns: 1fr; }
    }
    .loading { color: #8a8a96; padding: 40px 0; text-align: center; }
    .error-state { color: #8a8a96; padding: 40px 0; text-align: center; }
    .btn-retry {
      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
      padding: 8px 16px; font-size: 14px; cursor: pointer; margin-top: 12px;
      font-family: inherit;
    }
    .notification {
      display: flex; align-items: center; justify-content: space-between;
      padding: 12px 16px; border-radius: 8px; margin-bottom: 16px; font-size: 14px;
    }
    .notification.success { background: rgba(74, 222, 128, 0.1); color: #4ade80; }
    .notification.error { background: rgba(248, 113, 113, 0.1); color: #f87171; }
    .dismiss {
      background: none; border: none; color: inherit; cursor: pointer;
      font-size: 18px; line-height: 1;
    }
  `],
})
export class PlatformConnectionsComponent implements OnInit {
  private readonly service = inject(PlatformConnectionService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);

  readonly platformConfigs: PlatformConfig[] = [
    { platform: 'Blog', displayName: 'Blog', description: 'matthewkruczek.ai static site', connectionType: 'none' },
    { platform: 'Medium', displayName: 'Medium', description: 'Integration token authentication', connectionType: 'token' },
    { platform: 'Substack', displayName: 'Substack', description: 'Email/password authentication', connectionType: 'login' },
    { platform: 'LinkedIn', displayName: 'LinkedIn', description: 'OAuth 2.0 authentication', connectionType: 'oauth' },
    { platform: 'Twitter', displayName: 'Twitter / X', description: 'OAuth 2.0 with PKCE', connectionType: 'oauth' },
  ];

  platforms: PlatformStatus[] = [];
  loading = true;
  error = false;
  loadingPlatform: string | null = null;
  credentialErrors: Record<string, string> = {};
  notification: { type: 'success' | 'error'; message: string } | null = null;

  ngOnInit(): void {
    this.loadPlatforms();
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        if (params['connected']) {
          const knownPlatform = this.platformConfigs.find(c => c.platform === params['connected']);
          if (knownPlatform) {
            this.notification = {
              type: 'success',
              message: `${knownPlatform.displayName} connected successfully`,
            };
            this.loadPlatforms();
          }
        }
        if (params['error']) {
          this.notification = {
            type: 'error',
            message: 'Authentication failed. Please try again.',
          };
        }
      });
  }

  loadPlatforms(): void {
    this.loading = true;
    this.error = false;
    this.service.getPlatforms().subscribe({
      next: (platforms) => {
        this.platforms = platforms;
        this.loading = false;
      },
      error: () => {
        this.error = true;
        this.loading = false;
      },
    });
  }

  getStatus(platform: string): PlatformStatus | null {
    return this.platforms.find((p) => p.platform === platform) ?? null;
  }

  onConnect(platform: string): void {
    this.document.location!.href = this.service.getAuthorizeUrl(platform);
  }

  onDisconnect(platform: string): void {
    this.loadingPlatform = platform;
    this.service.disconnect(platform).subscribe({
      next: () => {
        this.loadingPlatform = null;
        this.loadPlatforms();
      },
      error: () => {
        this.loadingPlatform = null;
        this.notification = {
          type: 'error',
          message: `Failed to disconnect ${platform}`,
        };
      },
    });
  }

  onCredentialsSubmitted(event: {
    platform: string;
    credentials: StoreCredentialsRequest;
  }): void {
    this.loadingPlatform = event.platform;
    const { [event.platform]: _, ...rest } = this.credentialErrors;
    this.credentialErrors = rest;
    this.service.storeCredentials(event.platform, event.credentials).subscribe({
      next: () => {
        this.loadingPlatform = null;
        this.notification = {
          type: 'success',
          message: `${event.platform} credentials saved`,
        };
        this.loadPlatforms();
      },
      error: (err) => {
        this.loadingPlatform = null;
        this.credentialErrors = {
          ...this.credentialErrors,
          [event.platform]: err.error?.message ?? 'Failed to save credentials',
        };
      },
    });
  }
}
