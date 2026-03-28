import { Component, input, output } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Card } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { Badge } from 'primeng/badge';
import { Platform } from '../../../shared/models';
import { PLATFORM_ICONS, PLATFORM_LABELS, PLATFORM_COLORS } from '../../../shared/utils/platform-icons';

@Component({
  selector: 'app-platform-card',
  standalone: true,
  imports: [CommonModule, Card, ButtonModule, Tag, Badge, DatePipe],
  template: `
    <p-card styleClass="h-full">
      <div class="flex flex-column gap-3">
        <div class="flex align-items-center gap-3">
          <div class="flex align-items-center justify-content-center border-round" [style]="{ 'background-color': getColor(), width: '3rem', height: '3rem' }">
            <i [class]="getIcon()" style="font-size: 1.5rem; color: white"></i>
          </div>
          <div class="flex-1">
            <h3 class="m-0">{{ getLabel() }}</h3>
            <p-tag
              [value]="platform().isConnected ? 'Connected' : 'Disconnected'"
              [severity]="platform().isConnected ? 'success' : 'danger'"
            />
          </div>
        </div>

        @if (platform().isConnected) {
          @if (platform().lastSyncAt) {
            <div class="text-sm text-color-secondary">
              Last sync: {{ platform().lastSyncAt | date:'short' }}
            </div>
          }
          @if (platform().tokenExpiresAt) {
            <div class="text-sm text-color-secondary">
              Token expires: {{ platform().tokenExpiresAt | date:'short' }}
            </div>
          }
        }

        <div class="flex gap-2">
          @if (platform().isConnected) {
            <p-button label="Details" severity="info" size="small" [text]="true" (onClick)="details.emit()" />
            <p-button label="Test Post" severity="secondary" size="small" [text]="true" (onClick)="testPost.emit()" />
            <p-button label="Disconnect" severity="danger" size="small" [text]="true" (onClick)="disconnect.emit()" />
          } @else {
            <p-button label="Connect" icon="pi pi-link" (onClick)="connect.emit()" />
          }
        </div>
      </div>
    </p-card>
  `,
})
export class PlatformCardComponent {
  platform = input.required<Platform>();
  connect = output<void>();
  disconnect = output<void>();
  details = output<void>();
  testPost = output<void>();

  getIcon() { return PLATFORM_ICONS[this.platform().type]; }
  getLabel() { return PLATFORM_LABELS[this.platform().type]; }
  getColor() { return PLATFORM_COLORS[this.platform().type]; }
}
