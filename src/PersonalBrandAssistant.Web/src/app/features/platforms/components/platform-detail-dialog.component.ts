import { Component, input } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Dialog } from 'primeng/dialog';
import { Tag } from 'primeng/tag';
import { Chip } from 'primeng/chip';
import { Platform } from '../../../shared/models';

@Component({
  selector: 'app-platform-detail-dialog',
  standalone: true,
  imports: [CommonModule, Dialog, Tag, Chip, DatePipe],
  template: `
    <p-dialog header="Platform Details" [(visible)]="visible" [modal]="true" [style]="{ width: '450px' }">
      @if (platform) {
        <div class="flex flex-column gap-3">
          <div><strong>Display Name:</strong> {{ platform.displayName }}</div>
          <div>
            <strong>Status:</strong>
            <p-tag [value]="platform.isConnected ? 'Connected' : 'Disconnected'" [severity]="platform.isConnected ? 'success' : 'danger'" />
          </div>
          @if (platform.tokenExpiresAt) {
            <div><strong>Token Expires:</strong> {{ platform.tokenExpiresAt | date:'medium' }}</div>
          }
          @if (platform.lastSyncAt) {
            <div><strong>Last Sync:</strong> {{ platform.lastSyncAt | date:'medium' }}</div>
          }
          @if (platform.grantedScopes && platform.grantedScopes.length > 0) {
            <div>
              <strong>Scopes:</strong>
              <div class="flex flex-wrap gap-1 mt-1">
                @for (scope of platform.grantedScopes; track scope) {
                  <p-chip [label]="scope" />
                }
              </div>
            </div>
          }
        </div>
      }
    </p-dialog>
  `,
})
export class PlatformDetailDialogComponent {
  visible = false;
  platform?: Platform;

  open(platform: Platform) {
    this.platform = platform;
    this.visible = true;
  }
}
