import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';
import { CalendarSlot } from '../../../shared/models';

@Component({
  selector: 'app-slot-card',
  standalone: true,
  imports: [DatePipe, StatusBadgeComponent],
  template: `
    <div class="slot-card" [class]="'platform-' + slot().platform.toLowerCase()">
      <div class="slot-time">{{ slot().scheduledAt | date:'HH:mm' }}</div>
      <div class="slot-info">
        <span class="slot-title">{{ slot().contentId ? 'Assigned' : 'Open' }}</span>
        <app-status-badge [status]="slot().status" />
      </div>
    </div>
  `,
  styles: `
    .slot-card {
      padding: 0.375rem 0.5rem;
      border-radius: 4px;
      background: var(--p-surface-800);
      border-left: 3px solid var(--p-surface-600);
      cursor: pointer;
      transition: border-color 0.2s;
      font-size: 0.75rem;

      &:hover { border-color: var(--p-primary-color); }
    }
    .slot-time { font-size: 0.7rem; color: var(--p-text-muted-color); }
    .slot-info { display: flex; align-items: center; justify-content: space-between; gap: 0.25rem; }
    .slot-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .platform-twitterx { border-left-color: #1da1f2; }
    .platform-linkedin { border-left-color: #0077b5; }
    .platform-instagram { border-left-color: #e4405f; }
    .platform-youtube { border-left-color: #ff0000; }
    .platform-reddit { border-left-color: #ff4500; }
    .platform-personalblog { border-left-color: #4ade80; }
    .platform-substack { border-left-color: #ff6719; }
  `,
})
export class SlotCardComponent {
  slot = input.required<CalendarSlot>();
}
