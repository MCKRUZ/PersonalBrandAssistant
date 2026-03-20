import { Component, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { Badge } from 'primeng/badge';
import { Popover } from 'primeng/popover';
import { RelativeTimePipe } from '../../pipes/relative-time.pipe';
import { NotificationStore } from '../../store/notification.store';

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonModule, Badge, Popover, RelativeTimePipe],
  template: `
    <span class="notification-bell" (click)="panel.toggle($event)">
      <i class="pi pi-bell" style="font-size: 1.25rem; cursor: pointer"></i>
      @if (store.unreadCount() > 0) {
        <p-badge [value]="store.unreadCount().toString()" severity="danger" />
      }
    </span>

    <p-popover #panel [style]="{ width: '350px' }">
      <div class="flex justify-content-between align-items-center mb-2">
        <h4 class="m-0">Notifications</h4>
        @if (store.unreadCount() > 0) {
          <p-button label="Mark All Read" [text]="true" size="small" (onClick)="store.markAllRead(undefined)" />
        }
      </div>
      @for (notification of store.notifications().slice(0, 10); track notification.id) {
        <div
          class="p-2 border-round mb-1 cursor-pointer"
          [class.surface-ground]="!notification.isRead"
          (click)="store.markRead(notification.id)"
        >
          <div class="font-semibold text-sm">{{ notification.title }}</div>
          <div class="text-sm text-color-secondary">{{ notification.message }}</div>
          <div class="text-xs text-color-secondary mt-1">{{ notification.createdAt | relativeTime }}</div>
        </div>
      }
      @if (store.notifications().length === 0) {
        <div class="text-center text-color-secondary py-3">No notifications</div>
      }
    </p-popover>
  `,
  styles: `
    .notification-bell { position: relative; display: inline-flex; align-items: center; }
    .notification-bell p-badge { position: absolute; top: -8px; right: -8px; }
    .cursor-pointer { cursor: pointer; }
  `,
})
export class NotificationBellComponent implements OnInit {
  readonly store = inject(NotificationStore);
  @ViewChild('panel') panel!: Popover;

  ngOnInit() {
    this.store.load(undefined);
  }
}
